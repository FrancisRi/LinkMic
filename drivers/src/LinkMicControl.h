#pragma once
#ifdef LINKMIC_CONTROL_HOST_TEST
#include "FakeKernel.h"
#else
#include <ntddk.h>
#endif
NTSTATUS LinkMicControlInitialize(PDRIVER_OBJECT driver);
void LinkMicControlShutdown();
void LinkMicControlSetReady(bool ready);
// WaveRT writes captured PCM through this function at <= DISPATCH_LEVEL.
void LinkMicReadCapture(unsigned char* output, unsigned long bytes);
