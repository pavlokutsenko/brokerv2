using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using PriceCheck.Collector.Runtime.Driver;

if (args.Length != 2 || !int.TryParse(args[0], out var pid) || !File.Exists(args[1]))
    throw new ArgumentException("Pass target PID and executable path");

using var stream = File.OpenRead(args[1]);
using var pe = new PEReader(stream);
var data = pe.PEHeaders.SectionHeaders.Single(section => section.Name == ".data");
using var device = new Lu4Device();
var baseAddress = device.GetProcessBase(pid);
Console.WriteLine($"base=0x{baseAddress:X} data=0x{data.VirtualAddress:X} size=0x{data.VirtualSize:X}");

var section = new byte[data.VirtualSize];
for (var offset = 0; offset < section.Length; offset += 0x100000)
{
    var size = Math.Min(0x100000, section.Length - offset);
    var page = device.Read(pid, baseAddress + (uint)data.VirtualAddress + (uint)offset, size);
    page.CopyTo(section, offset);
}

var checkedPointers = new HashSet<ulong>();
var candidates = 0;
var matches = new List<(ulong Rva, ulong Block)>();
for (var offset = 0; offset < section.Length - 32; offset += 8)
{
    var block = BinaryPrimitives.ReadUInt64LittleEndian(section.AsSpan(offset + 0x10));
    if (block < 0x1000000000 || block > 0x00007FFFFFFFFFFF) continue;
    candidates++;
    if (!checkedPointers.Add(block)) continue;
    try
    {
        var first = device.Read(pid, block, 64);
        var ascii = System.Text.Encoding.ASCII.GetString(first);
        if (ascii.Contains("None", StringComparison.Ordinal))
        {
            var rva = (ulong)(uint)data.VirtualAddress + (uint)offset;
            matches.Add((rva, block));
            Console.WriteLine($"FNamePool candidate rva=0x{rva:X} block=0x{block:X} first={Convert.ToHexString(first.AsSpan(0, 16))}");
        }
    }
    catch (Exception) { }
}
Console.WriteLine($"pointer slots={candidates} unique={checkedPointers.Count} matches={matches.Count}");

var poolRva = matches.Single(match => match.Block > 0x10000000000 &&
    device.Read(pid, match.Block, 8).AsSpan(2, 4).SequenceEqual("None"u8)).Rva;
Console.WriteLine($"FNamePool rva=0x{poolRva:X}");
Console.WriteLine($"FNamePool header={Convert.ToHexString(device.Read(pid, baseAddress + poolRva, 64))}");

ulong U64(ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(device.Read(pid, address, 8));
uint U32(ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(device.Read(pid, address, 4));
string Name(uint id)
{
    var block = U64(baseAddress + poolRva + 0x10 + ((ulong)(id >> 16) * 8));
    var entry = block + ((ulong)(id & 0xFFFF) * 2);
    var header = BinaryPrimitives.ReadUInt16LittleEndian(device.Read(pid, entry, 2));
    var length = header >> 6;
    if (length is < 1 or > 512) return $"invalid:{id}";
    return (header & 1) == 0
        ? System.Text.Encoding.UTF8.GetString(device.Read(pid, entry + 2, length))
        : System.Text.Encoding.Unicode.GetString(device.Read(pid, entry + 2, length * 2));
}

void InspectName(uint id)
{
    var block = U64(baseAddress + poolRva + 0x10 + ((ulong)(id >> 16) * 8));
    var entry = block + ((ulong)(id & 0xFFFF) * 2);
    var bytes = device.Read(pid, entry - 24, 56);
    Console.WriteLine($"name id={id} block=0x{block:X} entry=0x{entry:X} bytes={Convert.ToHexString(bytes)}");
}

var foundObjects = 0;
ulong gobjectsRva = 0;
for (var offset = 0; offset < section.Length - 8; offset += 8)
{
    var chunks = BinaryPrimitives.ReadUInt64LittleEndian(section.AsSpan(offset));
    if (chunks < 0x1000000000 || chunks > 0x00007FFFFFFFFFFF || (chunks & 7) != 0) continue;
    try
    {
        var firstChunk = U64(chunks);
        if (firstChunk < 0x1000000000 || firstChunk > 0x00007FFFFFFFFFFF) continue;
        var obj = U64(firstChunk + 18904UL * 0x18);
        if (obj < 0x1000000000 || obj > 0x00007FFFFFFFFFFF) continue;
        var objectHeader = device.Read(pid, obj, 0x20);
        var vtable = BinaryPrimitives.ReadUInt64LittleEndian(objectHeader);
        var index = BinaryPrimitives.ReadInt32LittleEndian(objectHeader.AsSpan(0xC));
        if (vtable < baseAddress + 0x0629B000 || vtable >= baseAddress + 0x07DCB000 || index != 18904) continue;
        var id = BinaryPrimitives.ReadUInt32LittleEndian(objectHeader.AsSpan(0x18));
        InspectName(id);
        var rva = (ulong)(uint)data.VirtualAddress + (uint)offset;
        Console.WriteLine($"GObjects rva=0x{rva:X} index18904={Name(id)} object=0x{obj:X}");
        foundObjects++;
        gobjectsRva = rva;
    }
    catch (Exception) { }
}
Console.WriteLine($"GObjects matches={foundObjects}");
if (foundObjects != 1) throw new InvalidOperationException("GObjects not uniquely identified");

var targets = new[] { "UserLogin", "UserPassword", "ConnectToLoginServer", "LoadLogin", "GetLastLogin",
    "SelectGameServer", "ServerID", "GameServerInfo", "GameServersInfo", "SelectCharacter",
    "OnSelectCharacter", "EnterWorld" };
var targetIds = new Dictionary<uint, string>();
var blockCount = U32(baseAddress + poolRva + 8);
var lastBytes = U32(baseAddress + poolRva + 12);
Console.WriteLine($"FNamePool blocks={blockCount + 1} lastBytes={lastBytes}");
for (uint blockIndex = 0; blockIndex <= blockCount && blockIndex < 256; blockIndex++)
{
    var block = U64(baseAddress + poolRva + 0x10 + (blockIndex * 8));
    var count = blockIndex == blockCount ? (int)lastBytes : 0x20000;
    if (count is < 4 or > 0x20000) continue;
    var content = device.Read(pid, block, count);
    foreach (var target in targets)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(target);
        var start = 0;
        while (start < content.Length)
        {
            var found = content.AsSpan(start).IndexOf(needle);
            if (found < 0) break;
            var pos = start + found - 2;
            if (pos >= 0 && pos % 2 == 0)
            {
                var id = (blockIndex << 16) | (uint)(pos / 2);
                if (Name(id) == target)
                {
                    targetIds[id] = target;
                    Console.WriteLine($"FName {target} id={id}");
                }
            }
            start += found + 1;
        }
    }
}

var chunkTable = U64(baseAddress + gobjectsRva);
ulong ObjectAt(int index)
{
    var chunk = U64(chunkTable + (ulong)(index / 65536) * 8);
    return U64(chunk + (ulong)(index % 65536) * 0x18);
}
var loginModeClass = U64(ObjectAt(19220) + 0x20);
var loginLibraryClass = U64(ObjectAt(19329) + 0x20);
var lobbyHudClass = U64(ObjectAt(19210) + 0x20);
var loginLibraryCdo = ObjectAt(25311);
var libraryVtable = U64(loginLibraryCdo);
Console.WriteLine($"NetLoginLibrary CDO=0x{loginLibraryCdo:X} vtable=0x{libraryVtable:X} slot77=0x{U64(libraryVtable + 77UL * 8):X}");
var classCache = new Dictionary<ulong, bool>();
bool IsDerivedFrom(ulong value, ulong expected)
{
    if (expected != loginModeClass) return IsDerivedFromCore(value, expected);
    if (classCache.TryGetValue(value, out var cached)) return cached;
    var ancestors = new List<ulong>();
    var current = value;
    var found = false;
    for (var depth = 0; depth < 12 && current != 0; depth++)
    {
        if (current == expected) { found = true; break; }
        if (classCache.TryGetValue(current, out cached)) { found = cached; break; }
        ancestors.Add(current);
        try { current = U64(current + 0x40); }
        catch (Exception) { break; }
    }
    foreach (var ancestor in ancestors) classCache[ancestor] = found;
    return found;
}
bool IsDerivedFromCore(ulong value, ulong expected)
{
    for (var depth = 0; depth < 12 && value != 0; depth++)
    {
        if (value == expected) return true;
        try { value = U64(value + 0x40); }
        catch (Exception) { break; }
    }
    return false;
}
for (var chunkIndex = 0; chunkIndex < 8; chunkIndex++)
{
    var chunk = U64(chunkTable + (ulong)chunkIndex * 8);
    if (chunk == 0) break;
    Console.WriteLine($"GObjects chunk {chunkIndex} at 0x{chunk:X}");
    for (var index = 0; index < 65536; index++)
    {
        ulong obj;
        try { obj = U64(chunk + (ulong)index * 0x18); }
        catch (Exception) { break; }
        if (obj < 0x1000000000 || obj > 0x00007FFFFFFFFFFF) continue;
        uint id;
        try { id = U32(obj + 0x18); }
        catch (Exception) { continue; }
        var isTarget = targetIds.TryGetValue(id, out var name);
        if (!isTarget)
        {
            try
            {
                var candidateClass = U64(obj + 0x10);
                if (candidateClass == loginLibraryClass || IsDerivedFrom(candidateClass, loginModeClass) ||
                    IsDerivedFrom(candidateClass, lobbyHudClass))
                    Console.WriteLine($"Instance index={chunkIndex * 65536 + index} name={Name(id)} class={Name(U32(candidateClass + 0x18))} ptr=0x{obj:X} superLogin={IsDerivedFrom(candidateClass, loginModeClass)} superLobby={IsDerivedFrom(candidateClass, lobbyHudClass)}");
            }
            catch (Exception) { }
            continue;
        }
        try
        {
            var outer = U64(obj + 0x20);
            var ownerName = outer == 0 ? "<none>" : Name(U32(outer + 0x18));
            var classPointer = U64(obj + 0x10);
            var className = Name(U32(classPointer + 0x18));
            var meta = device.Read(pid, obj, 0xC0);
            var propertySize = BinaryPrimitives.ReadUInt32LittleEndian(meta.AsSpan(0x58));
            var scriptLength = BinaryPrimitives.ReadUInt32LittleEndian(meta.AsSpan(0x68));
            var paramsSize = BinaryPrimitives.ReadUInt16LittleEndian(meta.AsSpan(0xB6));
            var functionFlags = BinaryPrimitives.ReadUInt32LittleEndian(meta.AsSpan(0xB0));
            var children = BinaryPrimitives.ReadUInt64LittleEndian(meta.AsSpan(0x48));
            var properties = BinaryPrimitives.ReadUInt64LittleEndian(meta.AsSpan(0x50));
            Console.WriteLine($"Object index={chunkIndex * 65536 + index} name={name} owner={ownerName} class={className} ptr=0x{obj:X} ownerPtr=0x{outer:X} flags=0x{functionFlags:X} params={paramsSize} props={propertySize} script={scriptLength} children=0x{children:X} fields=0x{properties:X}");
            if ((name == "ConnectToLoginServer" || name == "SelectGameServer" ||
                name == "SelectCharacter" || name == "EnterWorld") && properties != 0)
            {
                var fieldPointer = properties;
                for (var fieldIndex = 0; fieldIndex < 4 && fieldPointer != 0; fieldIndex++)
                {
                    var field = device.Read(pid, fieldPointer, 0x90);
                    var next = BinaryPrimitives.ReadUInt64LittleEndian(field.AsSpan(0x18));
                    var fieldId = BinaryPrimitives.ReadUInt32LittleEndian(field.AsSpan(0x20));
                    var fieldClass = BinaryPrimitives.ReadUInt64LittleEndian(field.AsSpan(0x08));
                    Console.WriteLine($"Field {fieldIndex} name={Name(fieldId)} class=0x{fieldClass:X} next=0x{next:X} tail={Convert.ToHexString(field.AsSpan(0x28, 0x48))}");
                    fieldPointer = next;
                }
            }
        }
        catch (Exception) { }
    }
}

void DumpStructFields(ulong type, string label)
{
    Console.WriteLine($"Schema {label} ptr=0x{type:X}");
    var fieldPointer = U64(type + 0x50);
    var seen = new HashSet<ulong>();
    for (var fieldIndex = 0; fieldIndex < 256 && fieldPointer != 0 && seen.Add(fieldPointer); fieldIndex++)
    {
        var field = device.Read(pid, fieldPointer, 0x90);
        var next = BinaryPrimitives.ReadUInt64LittleEndian(field.AsSpan(0x18));
        var fieldId = BinaryPrimitives.ReadUInt32LittleEndian(field.AsSpan(0x20));
        Console.WriteLine($"SchemaField {label} {fieldIndex} name={Name(fieldId)} ptr=0x{fieldPointer:X} data={Convert.ToHexString(field.AsSpan(0x28, 0x48))}");
        fieldPointer = next;
    }
}
DumpStructFields(ObjectAt(11257), "GameServerInfo");
DumpStructFields(ObjectAt(11258), "GameServersInfo");
DumpStructFields(loginModeClass, "LU4LoginMode");
DumpStructFields(loginLibraryClass, "NetLoginLibrary");
DumpStructFields(U64(ObjectAt(218957) + 0x10), "LogIn_GameMode_C");
DumpStructFields(ObjectAt(52742), "Wid_Login_Servers_Item_C");
DumpStructFields(ObjectAt(191365), "Wid_Login_Servers_C");
DumpStructFields(ObjectAt(218848), "Wid_Login_Servers_View_C");
var serverItemClass = ObjectAt(52742);
for (var chunkIndex = 0; chunkIndex < 8; chunkIndex++)
{
    var chunk = U64(chunkTable + (ulong)chunkIndex * 8);
    if (chunk == 0) break;
    for (var index = 0; index < 65536; index++)
    {
        ulong obj;
        try { obj = U64(chunk + (ulong)index * 0x18); }
        catch (Exception) { break; }
        if (obj < 0x1000000000 || obj > 0x00007FFFFFFFFFFF) continue;
        try
        {
            if (U64(obj + 0x10) != serverItemClass) continue;
            var structId = U32(obj + 0x318);
            var itemId = U32(obj + 0x368);
            var namePointer = U64(obj + 0x318 + 0x28);
            var nameLength = U32(obj + 0x318 + 0x30);
            var serverName = namePointer != 0 && nameLength is > 0 and < 128
                ? System.Text.Encoding.Unicode.GetString(device.Read(pid, namePointer, checked((int)nameLength * 2))).TrimEnd('\0')
                : "<empty>";
            Console.WriteLine($"ServerItem index={chunkIndex * 65536 + index} ptr=0x{obj:X} structId={structId} widgetId={itemId} name={serverName}");
        }
        catch (Exception) { }
    }
}
var listedTypes = new HashSet<ulong>();
for (var chunkIndex = 0; chunkIndex < 8; chunkIndex++)
{
    var chunk = U64(chunkTable + (ulong)chunkIndex * 8);
    if (chunk == 0) break;
    for (var index = 0; index < 65536; index++)
    {
        ulong obj;
        try { obj = U64(chunk + (ulong)index * 0x18); }
        catch (Exception) { break; }
        if (obj < 0x1000000000 || obj > 0x00007FFFFFFFFFFF) continue;
        try
        {
            var name = Name(U32(obj + 0x18));
            if (name.Contains("GameInstance", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("GameServer", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"NamedObject index={chunkIndex * 65536 + index} name={name} ptr=0x{obj:X}");
                if ((name.Contains("GameInstance", StringComparison.OrdinalIgnoreCase) ||
                     name is "LU4LoginMode" or "NetLoginLibrary") && listedTypes.Add(obj))
                    DumpStructFields(obj, name);
            }
        }
        catch (Exception) { }
    }
}
