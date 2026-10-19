package dev.linkmic.app;

import android.content.Context;
import androidx.appcompat.app.AlertDialog;
import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import java.util.*;

/** Display metadata never serves as the key used to revoke a computer. */
final class PairedComputersDialog {
    interface Forget {void selected(Set<String> ids,AlertDialog dialog);}
    static AlertDialog show(Context context,List<TrustedComputers.Computer> computers,Forget forget){
        if(computers.isEmpty())return new MaterialAlertDialogBuilder(context).setTitle(R.string.manage_devices).setMessage(R.string.no_paired_computers).setPositiveButton(R.string.close,null).show();
        List<TrustedComputers.Computer> rows=new ArrayList<>(computers);
        CharSequence[] labels=new CharSequence[rows.size()];boolean[] checked=new boolean[rows.size()];
        for(int i=0;i<rows.size();i++){
            TrustedComputers.Computer row=rows.get(i);
            labels[i]=(row.name.isEmpty()?context.getString(R.string.unnamed_computer):row.name)+"\n"+context.getString(R.string.computer_identity,row.id.substring(row.id.length()-8));
        }
        AlertDialog dialog=new MaterialAlertDialogBuilder(context).setTitle(R.string.select_computers)
            .setMultiChoiceItems(labels,checked,(which,index,selected)->{
                checked[index]=selected;int count=0;for(boolean value:checked)if(value)count++;
                AlertDialog current=(AlertDialog)which;current.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(count>0);current.getButton(AlertDialog.BUTTON_POSITIVE).setText(context.getString(R.string.forget_selected,count));
            }).setNegativeButton(R.string.close,null).setPositiveButton(context.getString(R.string.forget_selected,0),null).create();
        dialog.show();dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view->{
            Set<String> selected=new HashSet<>();for(int i=0;i<checked.length;i++)if(checked[i])selected.add(rows.get(i).id);
            if(!selected.isEmpty())forget.selected(Collections.unmodifiableSet(selected),dialog);
        });return dialog;
    }
}
