using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SuyunCraft
{
    partial class MainForm
    {
        ComboBox cmbSaveInstance;
        Button btnSaveInstances;
        bool refreshingSaveInstances;

        void RefreshSaveInstances(string selectedId = null)
        {
            if (cmbSaveInstance == null) return;
            selectedId = selectedId ?? cfg.SaveInstanceId;
            refreshingSaveInstances = true;
            try
            {
                cmbSaveInstance.Items.Clear();
                cmbSaveInstance.Items.Add(new SaveInstance { Id = "", Name = "Default directory (no save isolation)", Version = "" });
                try { foreach (var item in SaveIsolation.List(cfg.GameDir)) cmbSaveInstance.Items.Add(item); }
                catch (Exception ex) { statusBar.Text = "Failed to read save instances: " + ex.Message; }
                var selected = cmbSaveInstance.Items.Cast<SaveInstance>().FirstOrDefault(i => i.Id == selectedId);
                if (selected == null && !string.IsNullOrEmpty(selectedId))
                {
                    selected = new SaveInstance { Id = selectedId, Name = "[Instance directory missing; please check] " + selectedId, Version = "?" };
                    cmbSaveInstance.Items.Add(selected); // Never silently fall back to the shared world directory.
                }
                cmbSaveInstance.SelectedItem = selected ?? cmbSaveInstance.Items[0];
                cfg.SaveInstanceId = selectedId ?? "";
                chkIsolation.Enabled = string.IsNullOrEmpty(cfg.SaveInstanceId);
            }
            finally { refreshingSaveInstances = false; }
            if (!string.IsNullOrEmpty(cfg.SaveInstanceId)) SaveInstanceChanged();
            else UpdateInstanceControls();
        }
        void SaveInstanceChanged()
        {
            if (refreshingSaveInstances || cmbSaveInstance.SelectedItem == null) return;
            var instance = (SaveInstance)cmbSaveInstance.SelectedItem;
            cfg.SaveInstanceId = instance.Id;
            chkIsolation.Enabled = instance.Id.Length == 0;
            if (instance.Id.Length > 0)
            {
                var item = cmbVersion.Items.Cast<object>().OfType<LocalVer>().FirstOrDefault(v => v.Id == instance.Version);
                if (item != null) { cmbVersion.SelectedItem = item; cfg.LastVersion = instance.Version; }
                statusBar.Text = item == null ? "The instance version is missing or corrupted; refusing to fall backdirectory。" : "Selected isolated instance: Save instance：" + instance.Name;
            }
            else statusBar.Text = "Default directory selected; the Home isolation toggle applies。";
            UpdateInstanceControls();
        }
        void UpdateInstanceControls()
        {
            if (cmbSaveInstance == null) return;
            bool available = !operationBusy && (gameProc == null || gameProc.HasExited);
            cmbSaveInstance.Enabled = available; btnSaveInstances.Enabled = available;
            cmbVersion.Enabled = available && string.IsNullOrEmpty(cfg.SaveInstanceId);
            chkIsolation.Enabled = available && string.IsNullOrEmpty(cfg.SaveInstanceId);
        }
        void ManageSaveInstances()
        {
            try
            {
                var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                using (var dialog = new SaveInstanceForm(snapshot, SelectedVersion(), () => gameProc != null && !gameProc.HasExited))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedInstance != null)
                    {
                        RefreshSaveInstances(dialog.SelectedInstance.Id); SaveInstanceChanged(); cfg.Save();
                    }
                    else RefreshSaveInstances();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Save isolation"); }
        }
    }
}
