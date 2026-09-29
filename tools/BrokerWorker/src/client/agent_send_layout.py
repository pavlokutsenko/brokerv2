"""Bounded PE export lookup of the native agent's versioned send descriptor."""
import struct


def read_agent_send_layout(read, base):
    header = read(base, 4096)
    if header[:2] != b'MZ': raise RuntimeError('ClientAgent DOS header is invalid')
    nt = struct.unpack_from('<I', header, 0x3c)[0]
    if not 64 <= nt <= 4096 - 264 or header[nt:nt+4] != b'PE\0\0':
        raise RuntimeError('ClientAgent PE header is invalid')
    if struct.unpack_from('<H', header, nt+24)[0] != 0x20b:
        raise RuntimeError('ClientAgent is not PE64')
    image_size = struct.unpack_from('<I', header, nt+80)[0]
    export, export_size = struct.unpack_from('<II', header, nt+136)

    def bounded(rva, size):
        if not rva or size <= 0 or size > 65536 or rva + size > image_size:
            raise RuntimeError('ClientAgent export points outside its image')
        return read(base+rva, size)

    directory = bounded(export, 40)
    functions, names, addresses, strings, ordinals = struct.unpack_from('<IIIII', directory, 20)
    if not 0 < names <= functions <= 4096:
        raise RuntimeError('ClientAgent export table size is invalid')
    name_rvas = bounded(strings, names*4)
    ordinal_table = bounded(ordinals, names*2)
    function_table = bounded(addresses, functions*4)
    for index in range(names):
        rva = struct.unpack_from('<I', name_rvas, index*4)[0]
        name = bounded(rva, min(128, image_size-rva)).split(b'\0', 1)[0]
        if name != b'PriceCheckSendHookLayout': continue
        ordinal = struct.unpack_from('<H', ordinal_table, index*2)[0]
        if ordinal >= functions: raise RuntimeError('ClientAgent export ordinal is invalid')
        descriptor = struct.unpack_from('<I', function_table, ordinal*4)[0]
        if export <= descriptor < export + export_size:
            raise RuntimeError('ClientAgent send descriptor cannot be a forwarded export')
        magic, size, version, reserved, callback, slot = struct.unpack('<IIIIQQ', bounded(descriptor, 32))
        if (magic, size, version, reserved) != (0x5043534c, 32, 1, 0):
            raise RuntimeError('ClientAgent send descriptor version is unsupported')
        if not base < callback < base+image_size or not base < slot <= base+image_size-8:
            raise RuntimeError('ClientAgent send descriptor pointers are invalid')
        return callback, slot
    raise RuntimeError('ClientAgent send descriptor is missing; rebuild the native agent')
