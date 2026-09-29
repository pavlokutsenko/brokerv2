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
        var (user,password) = profile.ActiveLogin();

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
        view.Write(12, profile.LoginServerId);
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
            while (deadline.Elapsed < TimeSpan.FromSeconds(LaunchTimeouts.LoginSeconds))
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
        throw new TimeoutException($"Auto login did not finish within 180 seconds (stage: {StageLabel(view.ReadInt32(8))}"+
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
            throw new InvalidOperationException("Profile market does not match the login server. Auto login stopped to prevent sending data to the wrong market.");
        var (user,password) = profile.ActiveLogin();
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) ||
            user.Length > 120 || password.Length > 120 ||
            user.Length + password.Length + 2 > 256 ||
            user.Contains('\0') || password.Contains('\0'))
            throw new InvalidOperationException("Auto login requires a username and password of at most 120 characters.");
        if (profile.LoginServerId is < 1 or > 1000)
            throw new InvalidOperationException("Select a game server ID from 1 to 1000.");
        if (profile.CharacterSlot is < 0 or > 6)
            throw new InvalidOperationException("Select a character slot from 0 to 6.");

        var dll = Path.Combine(AppContext.BaseDirectory, "ClientLaunchRuntime", "PriceCheck.ClientLogin.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Auto login module was not found.", dll);
    }

    private static string ErrorMessage(int status) => status switch
    {
            -3 => "Could not verify the LU4 executable layout for auto login.",
            -40 => "Auto login: verified object and name tables were not found in the loaded client.",
            -41 => "Auto login: login-function layout changed; check the log and client build.",
            -42 => "Auto login: object table changed during login.",
            -30 => "Login stopped: HWID or proxy protection was not verified.",
            -31 => "Character screen opened, but protected traffic to the selected world was not verified; automatic character selection stopped.",
            -15 => "The server selection screen did not appear in time.",
            -24 => "The character selection screen did not appear in time.",
            -26 => "The client returned an invalid character list.",
            -27 => "The character-list function differs from the validated client build.",
            -28 => "Could not attach the character roster observer for rotation. See character-roster-<PID>.txt in the logs folder.",
            _ => $"Auto login stopped at {StageLabel(status)} (code {status})."
    };

    private static bool IsAlive(int pid)
    {
        return PriceCheck.Windows.ClientProcessIdentity.Read(pid) is not null;
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
