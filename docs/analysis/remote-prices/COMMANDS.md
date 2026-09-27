# Reproduction

PowerShell only. Runtime state/captures live under LocalAppData. Rediscover PID;
never copy previous-process pointers. Do not run game command lanes concurrently.

```powershell
$clientPid = 19660
$research = Join-Path $env:LOCALAPPDATA "PriceCheckCollector/research/remote-prices/$clientPid"
New-Item -ItemType Directory -Path $research -Force
python tools/BrokerWorker/src/diagnostics/scan_lu4_actors.py $clientPid --json "$research/actors.json"
python tools/BrokerWorker/src/diagnostics/resolve_target_route.py $clientPid --snapshot "$research/actors.json" --json "$research/route.json"
python tools/BrokerWorker/src/diagnostics/discover_unreal_globals.py $clientPid --json "$research/globals.json"
python tools/BrokerWorker/src/diagnostics/inspect_shop_ufunctions.py $clientPid --globals "$research/globals.json" --start 17500 --stop 21000 --terms shop store market trader broker --json "$research/functions.json"
python tools/BrokerWorker/src/diagnostics/inspect_shop_ufunctions.py $clientPid --globals "$research/globals.json" --start 18600 --stop 19200 --terms request client price trade broker item --json "$research/all-functions.json"
```

Review current ownership/prologues; installer refuses unknown hooks. Always
restore after experiments, including partial installation with saved state:

```powershell
try {
    python tools/RemotePrices/packet_capture.py $clientPid install
    python tools/RemotePrices/broker_control.py $clientPid install-bridge
    python tools/RemotePrices/broker_sweep.py $clientPid --name broker-sweep-1
    python tools/RemotePrices/trader_enchant.py $clientPid 1291927129 --name remote-ABAPKA-enchant
} finally {
    python tools/RemotePrices/broker_control.py $clientPid restore-bridge
    python tools/RemotePrices/packet_capture.py $clientPid restore
}
```

ObjectID above is session-specific; resolve the nickname again. shop_control.py
normal-open/selected-control intentionally use a target and are not solutions.
Its empty-array mode accepts no transaction entries and has not returned prices.

```powershell
python tools/RemotePrices/shop_wire.py "$research/manual-shop-reference.json"
python -m unittest discover -s tools/RemotePrices -p 'test_*.py'
.\build.ps1 -SkipBroker -OutputDirectory workspace/remote-prices-build
```

lab.py reads bounded RVA disassembly and resolves PE runtime-function fragments.
A fragment boundary is not necessarily a complete logical function.

Additional research tools from observation002:

- send_probe.py install/snapshot/restore observes native send return values.
- winsock_capture.py install/dump/restore observes the existing lower callback.
- direct_control.py install/restore validates the current nested relay;
  open/empty-array --object-id ID --name NAME use the isolated direct channel.
  Requires same-PID latest_session.json and latest_active64_state.json.
- controller_control.py demonstrates the failed higher-level native route.
- compare_price_evidence.py compares a later complete A1 against earlier files.

For direct controls, install packet capture, ProcessEvent bridge and optional
send observers first, then the direct adapter. Restore in reverse order and
only where PID-scoped state exists. Direct open selects/approaches the trader;
it cannot demonstrate no-target success. Direct injections are recorded in
requests[].payload and bypass the native TX observer. No coordinate-spoofing
control was run; the user deferred that branch.
