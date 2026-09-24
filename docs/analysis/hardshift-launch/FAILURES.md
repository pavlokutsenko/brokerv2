# Failed paths and corrections

## Intermediate PID was mistaken for the game (2026-09-23)

An early collector build added `lu4-win64-shipping` to the PID claim list. It claimed this short-lived starter (PID 9908), then reported “client exited during Active Anticheat” while the real `lu4.bin` (PID 13556) continued running. The shipping process also loaded the startup agent, so its `ready` signal was misleading for game ownership. The claim list is again restricted to `lu4`/`lu4.bin`; the next live run bound `lu4.bin` PID 11052 and installed the receive-hook.

## Agent setup smoke failures

- Initial identity setup stopped at `GetAdaptersAddresses` because `iphlpapi.dll` was not loaded when `MH_CreateHookApi` looked it up. Explicitly loading `iphlpapi.dll` before creating that hook fixed it.
- The first .NET socket smoke test used `ConnectAsync`, which resolves a `ConnectEx` extension pointer through `WSAIoctl`. Hooking only `connect` and `WSAConnect` missed it. The agent now substitutes the `ConnectEx` pointer returned by `WSAIoctl` as well.

## Template UI automation (2026-09-23)

A UI Automation deletion probe matched the first element named “Новый шаблон”, which was not the template list item. Its selection call failed, and the following delete removed the previously selected template. The collector state was restored from the exact saved template identity and ID, then verified by a successful launch. Future UI Automation must match both `ControlType.ListItem` and the name, and stop the sequence on any failed action.
