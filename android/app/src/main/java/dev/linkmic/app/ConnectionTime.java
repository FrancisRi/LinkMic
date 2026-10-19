package dev.linkmic.app;
import java.util.Locale;

public final class ConnectionTime {
    private ConnectionTime(){}
    public static String format(long milliseconds){long seconds=Math.max(0,milliseconds/1000);return String.format(Locale.ROOT,"%02d:%02d:%02d",seconds/3600,(seconds/60)%60,seconds%60);}
}
