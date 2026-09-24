#pragma once

#include <windows.h>

// ATA IDENTIFY words 10-19 store the 20-character serial with bytes swapped
// inside each word. The source is the launch template's disk serial.
bool BuildAtaSerialFromEnvironment(BYTE (&out)[20]);
