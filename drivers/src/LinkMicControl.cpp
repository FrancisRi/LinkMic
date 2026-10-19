// LinkMic original PCM injection device; no WO Mic code or binaries.
#ifndef LINKMIC_CONTROL_HOST_TEST
#include <ntddk.h>
#include <wdmsec.h>
#endif
#include "LinkMicControl.h"
#include "LinkMicRing.h"

static LinkMicRing g_ring;
static KSPIN_LOCK g_lock;
static PDEVICE_OBJECT g_control;
static PDRIVER_DISPATCH g_previous[IRP_MJ_MAXIMUM_FUNCTION + 1];
static PFILE_OBJECT g_writer;
static bool g_accepting;
static bool g_deleteIssued;
static const GUID LinkMicControlClass = {0x6029a1a9,0x79b0,0x4719,{0xb5,0x2d,0xc9,0xb2,0x51,0xa8,0x67,0x21}};

static NTSTATUS Complete(PIRP irp, NTSTATUS status, ULONG_PTR bytes = 0) {
    irp->IoStatus.Status = status;
    irp->IoStatus.Information = bytes;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

static NTSTATUS Dispatch(PDEVICE_OBJECT device, PIRP irp) {
    auto stack = IoGetCurrentIrpStackLocation(irp);
    if (device != g_control) return g_previous[stack->MajorFunction](device, irp);
    KIRQL oldIrql;
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR transferred = 0;
    switch (stack->MajorFunction) {
    case IRP_MJ_CREATE:
        KeAcquireSpinLock(&g_lock, &oldIrql);
        if (!g_accepting) status = STATUS_DEVICE_NOT_READY;
        else if (g_writer != nullptr) status = STATUS_SHARING_VIOLATION;
        else { g_writer = stack->FileObject; g_ring.Reset(); status = STATUS_SUCCESS; }
        KeReleaseSpinLock(&g_lock, oldIrql);
        break;
    case IRP_MJ_CLEANUP:
    case IRP_MJ_CLOSE:
        KeAcquireSpinLock(&g_lock, &oldIrql);
        if (g_writer == stack->FileObject) { g_ring.Reset(); g_writer = nullptr; }
        KeReleaseSpinLock(&g_lock, oldIrql);
        status = STATUS_SUCCESS;
        break;
    case IRP_MJ_WRITE:
        if (stack->Parameters.Write.Length != LINKMIC_FRAME_BYTES || irp->AssociatedIrp.SystemBuffer == nullptr) {
            status = STATUS_INVALID_BUFFER_SIZE; break;
        }
        KeAcquireSpinLock(&g_lock, &oldIrql);
        if (!g_accepting || g_writer != stack->FileObject) status = STATUS_DEVICE_NOT_READY;
        else {
            g_ring.PushFrame(static_cast<unsigned char*>(irp->AssociatedIrp.SystemBuffer));
            transferred = LINKMIC_FRAME_BYTES; status = STATUS_SUCCESS;
        }
        KeReleaseSpinLock(&g_lock, oldIrql);
        break;
    case IRP_MJ_DEVICE_CONTROL:
        if (stack->Parameters.DeviceIoControl.IoControlCode == LINKMIC_IOCTL_QUERY) {
            if (stack->Parameters.DeviceIoControl.InputBufferLength != 0 ||
                stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(LINKMIC_INFO) ||
                irp->AssociatedIrp.SystemBuffer == nullptr) { status = STATUS_BUFFER_TOO_SMALL; break; }
            const LINKMIC_INFO info = { sizeof(LINKMIC_INFO), LINKMIC_PROTOCOL_VERSION, LINKMIC_SAMPLE_RATE,
                LINKMIC_CHANNELS, LINKMIC_BITS_PER_SAMPLE, LINKMIC_FRAME_BYTES, LINKMIC_RING_BYTES, 1 };
            RtlCopyMemory(irp->AssociatedIrp.SystemBuffer, &info, sizeof(info));
            transferred = sizeof(info); status = STATUS_SUCCESS;
        } else if (stack->Parameters.DeviceIoControl.IoControlCode == LINKMIC_IOCTL_RESET) {
            if (stack->Parameters.DeviceIoControl.InputBufferLength != 0 || stack->Parameters.DeviceIoControl.OutputBufferLength != 0) {
                status = STATUS_INVALID_PARAMETER; break;
            }
            KeAcquireSpinLock(&g_lock, &oldIrql);
            if (!g_accepting || g_writer != stack->FileObject) status = STATUS_DEVICE_NOT_READY;
            else { g_ring.Reset(); status = STATUS_SUCCESS; }
            KeReleaseSpinLock(&g_lock, oldIrql);
        }
        break;
    }
    return Complete(irp, status, transferred);
}

NTSTATUS LinkMicControlInitialize(PDRIVER_OBJECT driver) {
    UNICODE_STRING name = RTL_CONSTANT_STRING(L"\\Device\\LinkMicCapture");
    UNICODE_STRING link = RTL_CONSTANT_STRING(L"\\DosDevices\\LinkMicCapture");
    // System/admin and interactive desktop users only. No anonymous/network access.
    UNICODE_STRING sddl = RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)");
    KeInitializeSpinLock(&g_lock);
    g_ring.Reset(); g_writer = nullptr; g_accepting = false; g_deleteIssued = false;
    NTSTATUS status = IoCreateDeviceSecure(driver, 0, &name, FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN, TRUE, &sddl, &LinkMicControlClass, &g_control);
    if (!NT_SUCCESS(status)) return status;
    g_control->Flags |= DO_BUFFERED_IO;
    status = IoCreateSymbolicLink(&link, &name);
    if (!NT_SUCCESS(status)) { IoDeleteDevice(g_control); g_control = nullptr; return status; }
    for (unsigned int major = 0; major <= IRP_MJ_MAXIMUM_FUNCTION; ++major) {
        g_previous[major] = driver->MajorFunction[major];
        driver->MajorFunction[major] = Dispatch;
    }
    g_control->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}

void LinkMicReadCapture(unsigned char* output, unsigned long bytes) {
    // Bound lock-held copies even after a scheduler stall. WaveRT passes DMA spans.
    while (bytes != 0) {
        unsigned long chunk = bytes > LINKMIC_FRAME_BYTES ? LINKMIC_FRAME_BYTES : bytes;
        KIRQL oldIrql;
        KeAcquireSpinLock(&g_lock, &oldIrql);
        g_ring.Read(output, chunk);
        KeReleaseSpinLock(&g_lock, oldIrql);
        output += chunk; bytes -= chunk;
    }
}

void LinkMicControlShutdown() {
    if (g_control == nullptr) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_lock, &oldIrql);
    if (g_deleteIssued) { KeReleaseSpinLock(&g_lock, oldIrql); return; }
    g_deleteIssued = true;
    g_accepting = false; g_ring.Reset(); g_writer = nullptr;
    KeReleaseSpinLock(&g_lock, oldIrql);
    UNICODE_STRING link = RTL_CONSTANT_STRING(L"\\DosDevices\\LinkMicCapture");
    IoDeleteSymbolicLink(&link);
    // Keep both the dispatch wrapper and this identity until DriverUnload.
    // IoDeleteDevice marks an open control DO delete-pending; its existing file
    // handles still send CLEANUP/CLOSE, which must never reach PortCls (its DO
    // extension layout differs). The pointer is compared only, never dereferenced
    // after this call. Single-adapter policy prevents a new control DO replacing it.
    IoDeleteDevice(g_control);
}

void LinkMicControlSetReady(bool ready) {
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_lock, &oldIrql);
    g_accepting = ready && !g_deleteIssued;
    g_ring.Reset();
    KeReleaseSpinLock(&g_lock, oldIrql);
}
