/* Kernel EDID view shares the managed/native UUID + display-instance FNV mapping. */
static ULONG IdentityHashText(ULONG hash, PCWSTR text)
{
    while (*text) {
        WCHAR ch = *text++;
        if (ch >= L'A' && ch <= L'Z') ch += 32;
        hash = (hash ^ ch) * 16777619u;
    }
    return hash;
}

static VOID IdentityPatchEdid(PUCHAR data, ULONG size, PCWSTR uuid, PCWSTR instance)
{
    const UCHAR header[] = {0,255,255,255,255,255,255,0};
    UCHAR base[128];
    ULONG block, i, sum, serial, other;
    const CHAR digits[] = "0123456789ABCDEF";
    CHAR text[12];
    if (size < 128 || size > 32768 || size % 128 || RtlCompareMemory(data, header, 8) != 8 ||
        ((ULONG)data[126] + 1) * 128 > size) return;
    for (block = 0; block < size; block += 128) {
        sum = 0; for (i = 0; i < 128; ++i) sum += data[block + i];
        if (sum & 255) return;
    }
    RtlCopyMemory(base, data, 128);
    serial = IdentityHashText(2166136261u, L"edid|");
    serial = IdentityHashText(serial, uuid);
    serial = IdentityHashText(serial, L"|");
    serial = IdentityHashText(serial, instance);
    if (!serial) serial = 1;
    for (i = 0; i < 4; ++i) base[12 + i] = (UCHAR)(serial >> (8 * i));
    other = serial ^ 0x9E3779B9u;
    for (i = 0; i < 8; ++i) text[i] = digits[(serial >> (28 - 4 * i)) & 15];
    for (i = 0; i < 4; ++i) text[8 + i] = digits[(other >> (28 - 4 * i)) & 15];
    for (block = 54; block <= 108; block += 18) {
        if (base[block] || base[block + 1] || base[block + 2] || base[block + 3] != 0xFF || base[block + 4]) continue;
        RtlCopyMemory(base + block + 5, text, 12); base[block + 17] = 10;
    }
    sum = 0; for (i = 0; i < 127; ++i) sum += base[i];
    base[127] = (UCHAR)(0u - sum);
    RtlCopyMemory(data, base, 128);
}
