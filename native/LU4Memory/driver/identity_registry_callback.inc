/* Supported registry callbacks only. Captured input names; guarded output accesses. */
#include "identity_edid.inc"

static BOOLEAN IdentityNameIs(PUNICODE_STRING name, PCWSTR value)
{
    UNICODE_STRING match;
    if (!name || !name->Buffer) return FALSE;
    RtlInitUnicodeString(&match, value);
    return RtlEqualUnicodeString(name, &match, TRUE);
}

static BOOLEAN IdentityKeyPath(PVOID object, PWCHAR path, ULONG count)
{
    PCUNICODE_STRING name = NULL;
    ULONG i, chars;
    NTSTATUS status = CmCallbackGetKeyObjectIDEx(&g_IdentityCookie, object, NULL, &name, 0);
    if (!NT_SUCCESS(status) || !name) return FALSE;
    chars = name->Length / sizeof(WCHAR);
    if (chars >= count) { CmCallbackReleaseKeyObjectIDEx(name); return FALSE; }
    RtlCopyMemory(path, name->Buffer, name->Length); path[chars] = 0;
    CmCallbackReleaseKeyObjectIDEx(name);
    for (i = 0; i < chars; ++i) if (path[i] >= L'A' && path[i] <= L'Z') path[i] += 32;
    return TRUE;
}

static PCWSTR IdentityGuidForKey(PCWSTR path, PUNICODE_STRING name, PLU4_PROCESS_IDENTITY_REQUEST config)
{
    PCWSTR profile;
    const WCHAR root[] = L"\\registry\\machine\\system\\";
    const WCHAR marker[] = L"\\control\\idconfigdb\\hardware profiles\\";
    if (IdentityNameIs(name, L"MachineGuid") &&
        wcscmp(path, L"\\registry\\machine\\software\\microsoft\\cryptography") == 0) return config->MachineGuid;
    if (IdentityNameIs(name, L"ProductId") && wcscmp(path,
        L"\\registry\\machine\\software\\microsoft\\windows nt\\currentversion") == 0) return config->ProductId;
    if (IdentityNameIs(name, L"SusClientId") && wcscmp(path,
        L"\\registry\\machine\\software\\microsoft\\windows\\currentversion\\windowsupdate") == 0) return config->SusClientId;
    if (IdentityNameIs(name, L"MachineId") && wcscmp(path,
        L"\\registry\\machine\\software\\microsoft\\sqmclient") == 0) return config->SqmMachineId;
    if (IdentityNameIs(name, L"VideoIdentifier") && wcsncmp(path, root, RTL_NUMBER_OF(root) - 1) == 0 &&
        wcsstr(path, L"\\control\\video\\")) return config->VideoIdentifier;
    if (IdentityNameIs(name, L"ComputerName") && wcsncmp(path, root, RTL_NUMBER_OF(root) - 1) == 0 &&
        wcsstr(path, L"\\control\\computername\\")) return config->ComputerName;
    if (IdentityNameIs(name, L"ProcessorNameString") && wcsstr(path,
        L"\\registry\\machine\\hardware\\description\\system\\centralprocessor\\") == path) return config->ProcessorModel;
    if (!IdentityNameIs(name, L"HwProfileGuid") || wcsncmp(path, root, RTL_NUMBER_OF(root) - 1)) return NULL;
    profile = wcsstr(path, marker);
    if (profile) profile += RTL_NUMBER_OF(marker) - 1;
    else { profile = wcsstr(path, L"\\hardware profiles\\"); if (profile) profile += 19; }
    if (profile && (wcscmp(profile, L"current") == 0 || (wcslen(profile) == 4 &&
        profile[0] >= L'0' && profile[0] <= L'9' && profile[1] >= L'0' && profile[1] <= L'9' &&
        profile[2] >= L'0' && profile[2] <= L'9' && profile[3] >= L'0' && profile[3] <= L'9'))) return config->HardwareProfileGuid;
    return NULL;
}

static BOOLEAN IdentityReplacement(PCWSTR path, PUNICODE_STRING name, PLU4_PROCESS_IDENTITY_REQUEST config,
    PULONG type, const UCHAR** data, PULONG size)
{
    PCWSTR text = IdentityGuidForKey(path, name, config);
    if (text) { *type = REG_SZ; *data = (const UCHAR*)text; *size = (ULONG)((wcslen(text) + 1) * 2); return TRUE; }
    if (IdentityNameIs(name, L"InstallDate") && wcscmp(path,
        L"\\registry\\machine\\software\\microsoft\\windows nt\\currentversion") == 0) {
        *type = REG_DWORD; *data = (const UCHAR*)&config->InstallDate; *size = sizeof(ULONG); return TRUE;
    }
    if (IdentityNameIs(name, L"Update Revision") && wcsstr(path,
        L"\\registry\\machine\\hardware\\description\\system\\centralprocessor\\") == path) {
        *type = REG_BINARY; *data = config->ProcessorRevision; *size = 8; return TRUE;
    }
    return FALSE;
}

static BOOLEAN IdentityMonitorInstance(PCWSTR path, PWCHAR instance, ULONG count)
{
    const WCHAR root[] = L"\\registry\\machine\\system\\";
    const WCHAR marker[] = L"\\enum\\display\\";
    const WCHAR suffix[] = L"\\device parameters";
    PCWSTR first, slash, last;
    SIZE_T size;
    if (wcsncmp(path, root, RTL_NUMBER_OF(root) - 1)) return FALSE;
    first = wcsstr(path, marker); if (!first) return FALSE;
    first += RTL_NUMBER_OF(marker) - 1;
    slash = wcschr(first, L'\\'); if (!slash || slash == first || !slash[1]) return FALSE;
    last = wcschr(slash + 1, L'\\');
    if (!last || last == slash + 1 || wcscmp(last, suffix)) return FALSE;
    size = (SIZE_T)(last - first); if (size >= count) return FALSE;
    RtlCopyMemory(instance, first, size * sizeof(WCHAR)); instance[size] = 0;
    return TRUE;
}

static NTSTATUS IdentityEmitGuid(KEY_VALUE_INFORMATION_CLASS kind, PVOID output, ULONG capacity,
    PULONG returned, PUNICODE_STRING name, ULONG type, const UCHAR* data, ULONG dataLength)
{
    UCHAR payload[512] = {0};
    ULONG offset, needed;
    if (!name || name->Length > 80 || dataLength > 256 || !returned) return STATUS_INVALID_PARAMETER;
    switch (kind) {
    case KeyValuePartialInformation:
        offset = (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION, Data);
        ((PKEY_VALUE_PARTIAL_INFORMATION)payload)->Type = type;
        ((PKEY_VALUE_PARTIAL_INFORMATION)payload)->DataLength = dataLength;
        break;
    case KeyValuePartialInformationAlign64:
        offset = (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION_ALIGN64, Data);
        ((PKEY_VALUE_PARTIAL_INFORMATION_ALIGN64)payload)->Type = type;
        ((PKEY_VALUE_PARTIAL_INFORMATION_ALIGN64)payload)->DataLength = dataLength;
        break;
    case KeyValueFullInformation:
    case KeyValueFullInformationAlign64:
        offset = (ULONG)FIELD_OFFSET(KEY_VALUE_FULL_INFORMATION, Name) + name->Length;
        offset = (offset + (kind == KeyValueFullInformationAlign64 ? 7 : 3)) &
            ~(kind == KeyValueFullInformationAlign64 ? 7u : 3u);
        ((PKEY_VALUE_FULL_INFORMATION)payload)->Type = type;
        ((PKEY_VALUE_FULL_INFORMATION)payload)->DataLength = dataLength;
        ((PKEY_VALUE_FULL_INFORMATION)payload)->DataOffset = offset;
        ((PKEY_VALUE_FULL_INFORMATION)payload)->NameLength = name->Length;
        RtlCopyMemory(((PKEY_VALUE_FULL_INFORMATION)payload)->Name, name->Buffer, name->Length);
        break;
    default: return STATUS_NOT_SUPPORTED;
    }
    needed = offset + dataLength;
    RtlCopyMemory(payload + offset, data, dataLength);
    *returned = needed;
    if (output && capacity) RtlCopyMemory(output, payload, capacity < needed ? capacity : needed);
    return capacity >= needed ? STATUS_SUCCESS : capacity < offset ? STATUS_BUFFER_TOO_SMALL : STATUS_BUFFER_OVERFLOW;
}

static BOOLEAN IdentityKnownRegistryName(PUNICODE_STRING name)
{
    return IdentityNameIs(name, L"MachineGuid") || IdentityNameIs(name, L"HwProfileGuid") ||
        IdentityNameIs(name, L"ProductId") || IdentityNameIs(name, L"SusClientId") || IdentityNameIs(name, L"MachineId") ||
        IdentityNameIs(name, L"VideoIdentifier") || IdentityNameIs(name, L"ComputerName") ||
        IdentityNameIs(name, L"ProcessorNameString") || IdentityNameIs(name, L"Update Revision") || IdentityNameIs(name, L"InstallDate");
}

static VOID IdentityEdidOutput(KEY_VALUE_INFORMATION_CLASS kind, PVOID output, ULONG capacity,
    PCWSTR path, PLU4_PROCESS_IDENTITY_REQUEST config)
{
    ULONG offset, size, type;
    WCHAR instance[512];
    if (!output || !IdentityMonitorInstance(path, instance, RTL_NUMBER_OF(instance))) return;
    if (kind == KeyValuePartialInformation && capacity >= (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION, Data)) {
        PKEY_VALUE_PARTIAL_INFORMATION info = output;
        offset = (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION, Data); size = info->DataLength; type = info->Type;
    } else if (kind == KeyValuePartialInformationAlign64 && capacity >= (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION_ALIGN64, Data)) {
        PKEY_VALUE_PARTIAL_INFORMATION_ALIGN64 info = output;
        offset = (ULONG)FIELD_OFFSET(KEY_VALUE_PARTIAL_INFORMATION_ALIGN64, Data); size = info->DataLength; type = info->Type;
    } else if ((kind == KeyValueFullInformation || kind == KeyValueFullInformationAlign64) &&
        capacity >= (ULONG)FIELD_OFFSET(KEY_VALUE_FULL_INFORMATION, Name)) {
        PKEY_VALUE_FULL_INFORMATION info = output;
        offset = info->DataOffset; size = info->DataLength; type = info->Type;
    } else return;
    if (type == REG_BINARY && offset <= capacity && size <= capacity - offset)
        IdentityPatchEdid((PUCHAR)output + offset, size, config->SystemUuid, instance);
}

static NTSTATUS IdentityRegistryCallback(PVOID context, PVOID notification, PVOID argument)
{
    REG_NOTIFY_CLASS operation = (REG_NOTIFY_CLASS)(ULONG_PTR)notification;
    LU4_PROCESS_IDENTITY_REQUEST config;
    WCHAR path[1024];
    UNREFERENCED_PARAMETER(context);
    if (operation != RegNtPreQueryValueKey && operation != RegNtPostQueryValueKey &&
        operation != RegNtPostEnumerateValueKey) return STATUS_SUCCESS;
    __try {
        if (operation == RegNtPreQueryValueKey) {
            PREG_QUERY_VALUE_KEY_INFORMATION info = argument;
            if (!info || !IdentityKnownRegistryName(info->ValueName)) return STATUS_SUCCESS;
        } else if (operation == RegNtPostQueryValueKey) {
            PREG_POST_OPERATION_INFORMATION post = argument;
            PREG_QUERY_VALUE_KEY_INFORMATION info;
            if (!post || !NT_SUCCESS(post->Status) || !post->PreInformation) return STATUS_SUCCESS;
            info = post->PreInformation;
            if (!IdentityNameIs(info->ValueName, L"EDID")) return STATUS_SUCCESS;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return STATUS_SUCCESS; }
    if (!IdentityConfigForCurrent(&config)) return STATUS_SUCCESS;
    __try {
        if (operation == RegNtPreQueryValueKey) {
            PREG_QUERY_VALUE_KEY_INFORMATION info = argument;
            const UCHAR* data;
            ULONG type, size;
            NTSTATUS status;
            if (!info || !IdentityKeyPath(info->Object, path, RTL_NUMBER_OF(path))) return STATUS_SUCCESS;
            if (!IdentityReplacement(path, info->ValueName, &config, &type, &data, &size) ||
                info->KeyValueInformationClass == KeyValueBasicInformation) return STATUS_SUCCESS;
            status = IdentityEmitGuid(info->KeyValueInformationClass, info->KeyValueInformation, info->Length,
                info->ResultLength, info->ValueName, type, data, size);
            if (status == STATUS_NOT_SUPPORTED) return STATUS_SUCCESS;
            return NT_SUCCESS(status) ? STATUS_CALLBACK_BYPASS : status;
        } else {
            PREG_POST_OPERATION_INFORMATION post = argument;
            if (!post || !NT_SUCCESS(post->Status) || !post->PreInformation) return STATUS_SUCCESS;
            if (operation == RegNtPostQueryValueKey) {
                PREG_QUERY_VALUE_KEY_INFORMATION info = post->PreInformation;
                if (IdentityNameIs(info->ValueName, L"EDID") && IdentityKeyPath(info->Object, path, RTL_NUMBER_OF(path)))
                    IdentityEdidOutput(info->KeyValueInformationClass, info->KeyValueInformation, info->Length, path, &config);
            } else {
                PREG_ENUMERATE_VALUE_KEY_INFORMATION info = post->PreInformation;
                PKEY_VALUE_FULL_INFORMATION full = info->KeyValueInformation;
                UNICODE_STRING name;
                const UCHAR* data;
                ULONG type, size;
                NTSTATUS status;
                if ((info->KeyValueInformationClass != KeyValueFullInformation && info->KeyValueInformationClass != KeyValueFullInformationAlign64) ||
                    !full || info->Length < (ULONG)FIELD_OFFSET(KEY_VALUE_FULL_INFORMATION, Name) ||
                    full->NameLength > 80 || full->NameLength % 2 ||
                    full->NameLength > info->Length - (ULONG)FIELD_OFFSET(KEY_VALUE_FULL_INFORMATION, Name) ||
                    !IdentityKeyPath(info->Object, path, RTL_NUMBER_OF(path))) return STATUS_SUCCESS;
                name.Buffer = full->Name; name.Length = name.MaximumLength = (USHORT)full->NameLength;
                if (IdentityReplacement(path, &name, &config, &type, &data, &size)) {
                    status = IdentityEmitGuid(info->KeyValueInformationClass, full, info->Length, info->ResultLength, &name, type, data, size);
                    if (status != STATUS_NOT_SUPPORTED) { post->ReturnStatus = status; return STATUS_CALLBACK_BYPASS; }
                } else if (IdentityNameIs(&name, L"EDID"))
                    IdentityEdidOutput(info->KeyValueInformationClass, full, info->Length, path, &config);
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return STATUS_SUCCESS; }
    return STATUS_SUCCESS;
}
