#pragma once

// Diagnostic logging is opt-in and records API categories only. It never
// writes identifier values, proxy credentials, packet contents or endpoints.
void TraceStart();
void TraceEvent(const char* category, unsigned detail = 0);
void TraceIoctl(unsigned code, unsigned input_size, unsigned output_size,
                unsigned returned, unsigned last_error, bool success, const void* caller);
void TraceWorldSendStack();
