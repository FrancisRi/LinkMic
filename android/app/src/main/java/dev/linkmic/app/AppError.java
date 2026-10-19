package dev.linkmic.app;

import java.io.IOException;

/** Stable error codes keep protocol/storage behavior independent of UI language. */
public final class AppError extends IOException {
    public enum Code {
        BAD_PAIRING_DATA, DISCONNECTED, DESKTOP_UPDATE_REQUIRED, COMPUTER_AUTH_FAILED,
        PAIR_REQUIRED, PAIRING_MISMATCH, CREDENTIAL_CHANGED, TRUST_UNAVAILABLE,
        TRUST_SAVE_FAILED, TOO_MANY_COMPUTERS, NEARBY_PERMISSION, BLUETOOTH_DISABLED,
        RECORDING_UNSUPPORTED, MIC_PERMISSION, MIC_INITIALIZATION, MIC_READ
    }
    public final Code code;
    public AppError(Code code){super(code.name());this.code=code;}
    public AppError(Code code,Throwable cause){super(code.name(),cause);this.code=code;}
}
