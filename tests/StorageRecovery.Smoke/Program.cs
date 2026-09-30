using System.Text.Json;
using System.IO;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

var root = Path.Combine(Path.GetTempPath(), "PriceCheck-storage-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var profiles = new ProfileStore(root);
var profile = new CollectorProfile { Name = "Gamma", City = "Giran",
    LoginName="primary-account",LoginPassword="primary-secret-test",
    RotationAccounts=[new(){LoginName="secondary-account",LoginPassword="secondary-secret-test"}],
    RotationAccountIndex=1,
    CenterZonesByCity = new() { ["Giran"] = new() { X = 82414, Y = 148117 } } };
await profiles.SaveAsync([profile]);
await profiles.SaveAsync([profile]);
var storedJson=File.ReadAllText(Path.Combine(root,"profiles.json"));
Check(!storedJson.Contains("primary-secret-test") && !storedJson.Contains("secondary-secret-test"),
    "all rotation passwords are protected in persisted settings");
File.WriteAllBytes(Path.Combine(root,"profiles.json"),new byte[300]);
var loaded = (await profiles.LoadAsync()).Single();
Check(loaded.Id == profile.Id && loaded.CenterZonesByCity["Giran"].X == 82414,"profile and center restored");
Check(loaded.RotationAccountIndex==0 && loaded.RotationAccounts.Single().LoginPassword=="secondary-secret-test" &&
      loaded.LoginPassword=="primary-secret-test","additional credentials restore without selecting them for the primary client");
var templates = new LaunchTemplateStore(root);
var template = new LaunchTemplate { Name = "Fixture", HardwareEnabled = false, ProxyEnabled = false };
await templates.SaveAsync([template]);await templates.SaveAsync([template]);
File.WriteAllText(Path.Combine(root,"launch-templates.json"),"{incomplete");
Check((await templates.LoadAsync()).Single().Id == template.Id,"template restored");

var bad = Path.Combine(root,"unrecoverable.json");
File.WriteAllBytes(bad,new byte[20]);File.WriteAllText(bad+".bak","null");
try { _=DurableJsonFile.ReadRecoverable<List<string>>(bad);throw new Exception("corrupt settings cannot silently become defaults"); }
catch (InvalidDataException) { }
Check(!File.Exists(bad) && Directory.GetFiles(root,"unrecoverable*.corrupt-*").Length == 2,"invalid settings retained for recovery");
try { _=DurableJsonFile.ReadRecoverable<List<string>>(bad);throw new Exception("next launch cannot silently replace quarantined settings"); }
catch (InvalidDataException) { }

var directory=Path.Combine(root,"outbox");Directory.CreateDirectory(directory);
var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var payload=new ServerUploadEnvelope {Kind="broker",Url="http://127.0.0.1:9/test-only",Body=JsonSerializer.SerializeToElement(new {batchId="fixture"})};
DurableJsonFile.Write(Path.Combine(directory,"pending.ready"),payload,options,false);
File.Move(Path.Combine(directory,"pending.ready"),Path.Combine(directory,"pending.ready.tmp"));
DurableJsonFile.Write(Path.Combine(directory,"interrupted.sending.123"),payload,options,false);
File.WriteAllBytes(Path.Combine(directory,"broken.ready.tmp"),new byte[50]);
// Cancelled before the loop: exercise startup recovery without any network request.
await ServerUploadWorker.RunAsync(directory,new CancellationToken(true));
Check(File.Exists(Path.Combine(directory,"pending.ready")),"flushed pending upload recovered");
Check(File.Exists(Path.Combine(directory,"interrupted.ready")),"in-flight upload returned to ready");
Check(Directory.GetFiles(Path.Combine(directory,"rejected"),"*.json").Length == 1,"broken partial upload quarantined");
var recovered=JsonSerializer.Deserialize<ServerUploadEnvelope>(File.ReadAllText(Path.Combine(directory,"pending.ready")),options)!;
Check(recovered.Id==payload.Id && recovered.Body.GetProperty("batchId").GetString()=="fixture","retry preserves idempotency identity");
Console.WriteLine($"STORAGE_RECOVERY_OK profiles templates incomplete_settings pending_upload in_flight_upload malformed_upload stable_identity; fixtures: {root}");
static void Check(bool value,string message) { if(!value)throw new Exception(message); }
