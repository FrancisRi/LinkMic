package dev.linkmic.app;

import android.content.Context;
import java.net.*;

/** Store the meaning, not already-translated text, so a recreated UI can translate again. */
final class UiStatus {
    final int message;
    final boolean needsPairing;
    private final Object[] arguments;
    private UiStatus(int message,boolean needsPairing,Object[] arguments){this.message=message;this.needsPairing=needsPairing;this.arguments=arguments.clone();}
    static UiStatus message(int message,Object...arguments){return new UiStatus(message,false,arguments);}
    boolean connected(){return message==R.string.connected_bluetooth||message==R.string.connected_wifi;}
    String text(Context context){return context.getString(message,arguments);}
    static UiStatus error(Throwable error){
        Throwable current=error;
        for(int depth=0;current!=null&&depth<8;depth++,current=current.getCause()){
            if(current instanceof AppError){
                AppError.Code code=((AppError)current).code;
                int resource=switch(code){
                    case BAD_PAIRING_DATA -> R.string.error_pairing_data;
                    case DISCONNECTED -> R.string.computer_disconnected;
                    case DESKTOP_UPDATE_REQUIRED -> R.string.error_desktop_update;
                    case COMPUTER_AUTH_FAILED -> R.string.error_computer_auth;
                    case PAIR_REQUIRED -> R.string.error_pair_required;
                    case PAIRING_MISMATCH -> R.string.error_pairing_mismatch;
                    case CREDENTIAL_CHANGED -> R.string.error_credentials_changed;
                    case TRUST_UNAVAILABLE -> R.string.trust_unavailable;
                    case TRUST_SAVE_FAILED -> R.string.error_trust_save;
                    case TOO_MANY_COMPUTERS -> R.string.error_too_many_computers;
                    case NEARBY_PERMISSION -> R.string.error_nearby_permission;
                    case BLUETOOTH_DISABLED -> R.string.error_bluetooth_disabled;
                    case RECORDING_UNSUPPORTED -> R.string.error_recording_unsupported;
                    case MIC_PERMISSION -> R.string.error_microphone_permission;
                    case MIC_INITIALIZATION -> R.string.error_microphone_init;
                    case MIC_READ -> R.string.error_microphone_read;
                };
                return new UiStatus(resource,code==AppError.Code.PAIR_REQUIRED||code==AppError.Code.PAIRING_MISMATCH||code==AppError.Code.CREDENTIAL_CHANGED,new Object[0]);
            }
            if(current instanceof SocketTimeoutException)return message(R.string.error_timeout);
            if(current instanceof UnknownHostException)return message(R.string.error_host);
            if(current instanceof SecurityException)return message(R.string.permissions_required);
        }
        return message(R.string.error_connection);
    }
}
