using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class ClientLoginService
{
    private const uint Magic = 0x50434C47;
    private const int MappingSize = 20 + 256 * sizeof(char);
    private const int CredentialsOffset = 20;

    public async Task EnterAsync(int pid, CollectorProfile profile, CancellationToken cancellationToken)
    {
        Validate(profile);
        var user = profile.LoginName;
        var password = profile.LoginPassword;

        var dll = Path.Combine(AppContext.BaseDirectory, "ClientLaunchRuntime", "PriceCheck.ClientLogin.dll");
        using var mapping = MemoryMappedFile.CreateNew($@"Local\PriceCheckAutoLogin_{pid}", MappingSize);
        using var view = mapping.CreateViewAccessor(0, MappingSize);
        using var roster=profile.CharacterRotationEnabled
            ? MemoryMappedFile.CreateNew($@"Local\PriceCheckCharacterRoster_{pid}",16) : null;
        using var rosterView=roster?.CreateViewAccessor(0,16);
        if(rosterView is not null)
        { rosterView.Write(0,0x50435253u);rosterView.Write(4,-1);rosterView.Write(8,-1);rosterView.Write(12,1u); }
        view.Write(0, Magic);
        view.Write(4, checked((ushort)user.Length));
        view.Write(6, checked((ushort)password.Length));
        view.Write(8, 0);
        view.Write(12, 1); // Gamma
        view.Write(16, profile.CharacterSlot);
        for (var index = 0; index < user.Length; index++) view.Write(CredentialsOffset + index * 2, user[index]);
        view.Write(CredentialsOffset + user.Length * 2, '\0');
        var passwordOffset = CredentialsOffset + (user.Length + 1) * 2;
        for (var index = 0; index < password.Length; index++) view.Write(passwordOffset + index * 2, password[index]);
        view.Write(passwordOffset + password.Length * 2, '\0');

        try
        {
            using var hook = await ClientAgentHookLoader.InstallAsync(pid, dll, cancellationToken);
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(28))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = view.ReadInt32(8);
                if (status == 4)
                {
                    if(rosterView is not null)
                    {
                        var count=rosterView.ReadInt32(4);var selected=rosterView.ReadInt32(8);
                        if(count is <1 or >7 || selected<0 || selected>=count)
                            throw new InvalidDataException("The client did not confirm its available character list.");
                        profile.RotationCharacterCount=count;profile.CharacterSlot=selected;
                        profile.RotationCharacterSlots=Enumerable.Range(0,count).ToArray();
                    }
                    return;
                }
                if (status < 0) throw new InvalidOperationException(ErrorMessage(status));
            if (!IsAlive(pid)) throw new InvalidOperationException("The client exited during auto login.");
                hook.Pulse();
                await Task.Delay(150, cancellationToken);
            }
        throw new TimeoutException($"Auto login did not finish within 28 seconds (stage: {StageLabel(view.ReadInt32(8))}"+
            (rosterView is null ? "" : $"; character count: {rosterView.ReadInt32(4)}")+").");
        }
        finally
        {
            view.Write(0, 0u);
            for (var index = 0; index < (user.Length + password.Length + 2) * 2; index++)
                view.Write(CredentialsOffset + index, (byte)0);
            view.Flush();
        }
    }

    public static void Validate(CollectorProfile profile)
    {
        if (!profile.Name.Equals(profile.LoginServerName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Рынок профиля не совпадает с сервером входа. Автовход остановлен, чтобы не отправить данные другого рынка.");
        var user = profile.LoginName;
        var password = profile.LoginPassword;
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) ||
            user.Length > 120 || password.Length > 120 ||
            user.Length + password.Length + 2 > 256 ||
            user.Contains('\0') || password.Contains('\0'))
            throw new InvalidOperationException("Auto login requires a username and password of at most 120 characters.");
        if (profile.LoginServerName != "Gamma")
            throw new InvalidOperationException("Auto login is currently validated only for the Gamma server.");
        if (profile.CharacterSlot is < 0 or > 6)
            throw new InvalidOperationException("Select a character slot from 0 to 6.");

        var dll = Path.Combine(AppContext.BaseDirectory, "ClientLaunchRuntime", "PriceCheck.ClientLogin.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Auto login module was not found.", dll);
    }

    private static string ErrorMessage(int status) => status switch
    {
            -3 => "This LU4 build differs from the validated build; programmatic login is not supported yet.",
            -15 => "The server selection screen did not appear in time.",
            -24 => "The character selection screen did not appear in time.",
            -26 => "The client returned an invalid character list.",
            -27 => "The character-list function differs from the validated client build.",
            -28 => "Could not observe the character list for automatic rotation.",
            _ => $"Auto login stopped at {StageLabel(status)} (code {status})."
    };

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static string StageLabel(int status) => status switch
    {
        <= -20 => "character selection",
        <= -11 => "server selection",
        < 0 => "account login",
        0 or 1 => "account login",
        2 => "server selection",
        3 => "waiting for character list after server choice",
        _ => "character selection"
    };
}
