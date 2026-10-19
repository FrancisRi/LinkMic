package dev.linkmic.app;

import android.content.Context;
import android.content.res.Configuration;
import android.os.LocaleList;
import java.util.Locale;

final class AppLanguage {
    static Locale select(Locale primary){return "zh".equals(primary.getLanguage())?Locale.SIMPLIFIED_CHINESE:Locale.ENGLISH;}
    static Context wrap(Context base){
        // Read the system-facing application context, not an already-localized Activity.
        // Only the first language decides; a secondary Chinese language must not override English.
        Context application=base.getApplicationContext();
        Configuration system=(application==null?base:application).getResources().getConfiguration();
        LocaleList locales=system.getLocales();
        Locale chosen=select(locales.isEmpty()?Locale.ENGLISH:locales.get(0));
        Configuration localized=new Configuration(base.getResources().getConfiguration());
        localized.setLocales(new LocaleList(chosen));localized.setLayoutDirection(chosen);
        return base.createConfigurationContext(localized);
    }
}
