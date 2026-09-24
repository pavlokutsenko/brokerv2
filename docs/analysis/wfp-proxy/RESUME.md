# Resume

The new WFP driver and `ProxyTcpBroker` passed benign socket and live LU4 local authenticated CONNECT tests, including world screenshots with and without a recently loaded driver. The supplied external proxy passed preflight and sustained world traffic twice; its scene was not captured. Release driver hash is `C7228FD5D285C29268A64707B3062FEEEFCCB76BEB5332CB8BF5B4E52CCC64DA`, packaged as `DriverRuntime/lu4_memory_wfp.sys`. The service was upgraded to that path, `build.ps1 -SkipBroker` passed, and the release Collector was restarted as PID 12800.

The `--two-clients` live smoke launched LU4 PIDs 10492 and 6956 through one process service and kept both alive with distinct generated UUIDs, agent readiness, radar sessions and WFP route bindings to separate listener ports. It did not test simultaneous logins or CONNECT traffic. Both test clients were stopped.

Outstanding: identify why Active Anticheat showed a Windows Test Mode dialog only on the immediate post-driver-load local-proxy run despite `testsigning No` and restored DSE. Later direct and local-proxy world runs on the already loaded release driver showed no dialog; this suggests a transient condition but does not prove its cause. Keep proxy TCP byte tracing opt-in (`PRICECHECK_TRACE_HARDWARE=1`). Avoid reviving the old pipe `recv` route; it fails after the first raw world reply.
