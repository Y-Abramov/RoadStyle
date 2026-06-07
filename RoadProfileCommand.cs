// RoadProfileCommand.cs
// Профили настроек дорог: сохранение/загрузка через прямой API AlignmentStyle.SaveToStg/LoadFromStg

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Topomatic.Alg.Model;
using Topomatic.Alg.Style;
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.Stg;

namespace RoadStyle
{
    public partial class RoadStylePlugin : PluginInitializator
    {
        [cmd("save_road_profile")]
        public void SaveRoadProfile()
        {
            var models = CollectRoadModels();
            if (models.Count == 0) { MessageDlg.Show("Нет открытых трасс."); return; }

            using (var form = new SaveProfileForm(models))
            {
                if (form.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var am = form.SelectedModel.ProjectModel.LockRead() as AlignmentModel;
                    if (am == null) { MessageDlg.Show("Не удалось получить модель."); return; }

                    var doc = BuildProfileDocument(am, form.SavePlan, form.SaveSurface);
                    doc.SaveToFileAsXml(form.SelectedPath);
                    MessageDlg.Show($"✓ Профиль сохранён:\n{form.SelectedPath}");
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    var inner = ex.InnerException;
                    while (inner != null) { msg += "\n→ " + inner.Message; inner = inner.InnerException; }
                    MessageDlg.Show("Ошибка сохранения:\n" + msg);
                }
            }
        }

        [cmd("load_road_profile")]
        public void LoadRoadProfile()
        {
            string profilePath;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title  = "Открыть профиль настроек";
                dlg.Filter = "Профиль настроек (*.rdprofile)|*.rdprofile|XML-файлы (*.xml)|*.xml";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                profilePath = dlg.FileName;
            }

            StgDocument doc;
            try
            {
                doc = new StgDocument();
                doc.LoadFromFileAsXml(profilePath);
            }
            catch (Exception ex)
            {
                MessageDlg.Show("Ошибка загрузки файла:\n" + ex.Message);
                return;
            }

            var meta = doc.Body.GetNode("meta");
            if (meta == null)
            {
                MessageDlg.Show("Файл не является профилем настроек дорог.");
                return;
            }

            bool hasPlan    = meta.GetBoolean("has_plan",    false);
            bool hasSurface = meta.GetBoolean("has_surface", false);

            var models = CollectRoadModels();
            if (models.Count == 0) { MessageDlg.Show("Нет открытых трасс."); return; }

            using (var form = new LoadProfileForm(models, profilePath, hasPlan, hasSurface))
            {
                if (form.ShowDialog() != DialogResult.OK) return;

                int applied = 0;
                var errors  = new List<string>();

                foreach (var target in form.SelectedModels)
                {
                    try
                    {
                        target.ProjectModel.LockWrite();
                        try
                        {
                            var am = target.ProjectModel.LockRead() as AlignmentModel;
                            if (am == null) continue;
                            ApplyProfileDocument(doc, am, form.ApplyPlan, form.ApplySurface);
                            target.ProjectModel.Modified = true;
                            applied++;
                        }
                        finally { target.ProjectModel.UnlockWrite(); }
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        var inner = ex.InnerException;
                        while (inner != null) { msg += " → " + inner.Message; inner = inner.InnerException; }
                        errors.Add(target.Name + ": " + msg);
                    }
                }

                if (errors.Count > 0)
                    MessageDlg.Show("Применено: " + applied + "\nОшибки:\n" + string.Join("\n", errors));
                else
                    MessageDlg.Show($"✓ Профиль применён к {applied} трасс(е).");
            }
        }

        private StgDocument BuildProfileDocument(AlignmentModel am, bool savePlan, bool saveSurface)
        {
            var doc  = new StgDocument();
            var body = doc.Body;

            var meta = body.AddNode("meta");
            meta.AddBoolean("has_plan",    savePlan);
            meta.AddBoolean("has_surface", saveSurface);

            if (savePlan && am.Alignment?.Style != null)
                am.Alignment.Style.SaveToStg(body.AddNode("plan_style"));

            if (saveSurface && am.Surface != null)
            {
                var surfNode     = body.AddNode("surface_style");
                var surfaceStyle = GetProp(am.Surface, "Style");
                if (surfaceStyle != null)
                {
                    foreach (var prop in new[] {
                        "HorizontalsStyle", "PointsStyle", "StructureLinesStyle",
                        "InclinationsStyle", "TrianglesStyle" })
                    {
                        var subStyle = GetProp(surfaceStyle, prop);
                        if (subStyle != null)
                            InvokeStgSave(subStyle, surfNode.AddNode(prop));
                    }
                }

                var visNode = surfNode.AddNode("visibility");
                foreach (var p in new[] { "Visible", "RibsVisible", "TrianglesVisible" })
                {
                    var val = GetProp(am.Surface, p);
                    if (val is bool bv) visNode.AddBoolean(p, bv);
                }
            }

            // DynamicSurface намеренно не сохраняется — зависит от ИМ каждой трассы
            return doc;
        }

        private void ApplyProfileDocument(StgDocument doc, AlignmentModel am, bool applyPlan, bool applySurface)
        {
            var body = doc.Body;

            if (applyPlan && am.Alignment?.Style != null && body.IsExists("plan_style"))
                am.Alignment.Style.LoadFromStg(body.GetNode("plan_style"));

            if (applySurface && am.Surface != null && body.IsExists("surface_style"))
            {
                var surfNode     = body.GetNode("surface_style");
                var surfaceStyle = GetProp(am.Surface, "Style");
                if (surfaceStyle != null)
                {
                    foreach (var prop in new[] {
                        "HorizontalsStyle", "PointsStyle", "StructureLinesStyle",
                        "InclinationsStyle", "TrianglesStyle" })
                    {
                        if (!surfNode.IsExists(prop)) continue;
                        var subNode  = surfNode.GetNode(prop);
                        var subStyle = GetProp(surfaceStyle, prop);
                        if (subStyle == null) continue;

                        // Сохраняем видимость ДО загрузки — LoadFromStg сбрасывает её
                        var visibleBefore = GetProp(subStyle, "Visible");
                        InvokeStgLoad(subStyle, subNode);
                        if (visibleBefore != null)
                            SetProp(subStyle, "Visible", visibleBefore);
                    }
                }
            }
            // DynamicSurface не применяется — зависит от ИМ каждой трассы
        }

        private List<RoadModelInfo> CollectRoadModels()
        {
            var models = new List<RoadModelInfo>();
            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var am = pm.LockRead() as AlignmentModel;
                    if (am?.Drawing != null)
                        models.Add(new RoadModelInfo(GetModelName(pm, am), pm, am.Drawing, ModelType.Road));
                }
                catch { }
                return false;
            });
            return models;
        }

        private void InvokeStgSave(object obj, StgNode node)
        {
            if (obj == null || node == null) return;
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "SaveToStg")
                    {
                        var prms = m.GetParameters();
                        if (prms.Length >= 1 && prms[0].ParameterType == typeof(StgNode))
                        {
                            try
                            {
                                if (prms.Length == 1) m.Invoke(obj, new object[] { node });
                                else                  m.Invoke(obj, new object[] { node, null });
                            }
                            catch (System.Reflection.TargetInvocationException tie)
                            {
                                throw tie.InnerException ?? tie;
                            }
                            return;
                        }
                    }
        }

        private void InvokeStgLoad(object obj, StgNode node)
        {
            if (obj == null || node == null) return;
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "LoadFromStg")
                    {
                        var prms = m.GetParameters();
                        if (prms.Length >= 1 && prms[0].ParameterType == typeof(StgNode))
                        {
                            try
                            {
                                if (prms.Length == 1) m.Invoke(obj, new object[] { node });
                                else                  m.Invoke(obj, new object[] { node, null });
                            }
                            catch (System.Reflection.TargetInvocationException tie)
                            {
                                throw tie.InnerException ?? tie;
                            }
                            return;
                        }
                    }
        }

        private object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, flags);
                if (p != null) return p.GetValue(obj);
            }
            return null;
        }

        private void SetProp(object obj, string name, object value)
        {
            if (obj == null) return;
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, flags);
                if (p != null && p.CanWrite) { p.SetValue(obj, value); return; }
            }
        }
    }

    // ── Диалог сохранения профиля ───────────────────────────────

    internal class SaveProfileForm : Form
    {
        private readonly List<RoadModelInfo> _models;
        private ComboBox _cmbSource;
        private CheckBox _chkPlan, _chkSurface;
        private TextBox  _txtPath;
        private Button   _btnBrowse, _btnOk, _btnCancel;

        public RoadModelInfo SelectedModel { get; private set; }
        public string        SelectedPath  { get; private set; }
        public bool          SavePlan      { get; private set; }
        public bool          SaveSurface   { get; private set; }

        public SaveProfileForm(List<RoadModelInfo> models)
        {
            _models = models;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "Экспортировать стиль";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(MakeBold("Источник настроек:", 12, y)); y += 22;
            _cmbSource = new ComboBox { Left = 12, Top = y, Width = 470, Height = 24, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in _models) _cmbSource.Items.Add(m.Name);
            _cmbSource.SelectedIndex = 0;
            Controls.Add(_cmbSource); y += 34;

            Controls.Add(MakeBold("Сохранить разделы:", 12, y)); y += 24;

            _chkPlan = new CheckBox { Text = "План (линия трассы, пикетаж, стиль отображения)", Left = 16, Top = y, Width = 460, Height = 20, Checked = true };
            Controls.Add(_chkPlan); y += 26;

            _chkSurface = new CheckBox { Text = "Проектная поверхность (горизонтали, точки, рёбра, уклоны)", Left = 16, Top = y, Width = 460, Height = 20, Checked = true };
            Controls.Add(_chkSurface); y += 34;

            Controls.Add(MakeBold("Сохранить в файл:", 12, y)); y += 22;
            _txtPath = new TextBox { Left = 12, Top = y, Width = 380, Height = 24, ReadOnly = true };
            Controls.Add(_txtPath);

            _btnBrowse = new Button { Text = "...", Left = 398, Top = y, Width = 84, Height = 24 };
            _btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title           = "Экспортировать стиль";
                    dlg.Filter          = "Профиль (*.rdprofile)|*.rdprofile";
                    dlg.DefaultExt      = "rdprofile";
                    dlg.FileName        = _models[_cmbSource.SelectedIndex].Name + "_style";
                    dlg.OverwritePrompt = true;
                    if (dlg.ShowDialog() == DialogResult.OK) _txtPath.Text = dlg.FileName;
                }
            };
            Controls.Add(_btnBrowse); y += 36;

            _btnOk = new Button { Text = "Экспортировать", Left = 272, Top = y, Width = 108, Height = 28 };
            _btnOk.Click += (s, e) =>
            {
                if (!_chkPlan.Checked && !_chkSurface.Checked)
                { MessageDlg.Show("Выберите хотя бы один раздел."); return; }
                if (string.IsNullOrEmpty(_txtPath.Text))
                { MessageDlg.Show("Укажите путь для сохранения."); return; }
                SelectedModel = _models[_cmbSource.SelectedIndex];
                SelectedPath  = _txtPath.Text;
                SavePlan      = _chkPlan.Checked;
                SaveSurface   = _chkSurface.Checked;
                DialogResult  = DialogResult.OK;
                Close();
            };
            Controls.Add(_btnOk);

            _btnCancel = new Button { Text = "Отмена", Left = 390, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(_btnCancel);

            Height = y + 68;
            CancelButton = _btnCancel;
        }

        private Label MakeBold(string text, int left, int top) => new Label
        {
            Text = text, Left = left, Top = top, Width = 470, Height = 20,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
        };
    }

    // ── Диалог загрузки профиля ─────────────────────────────────

    internal class LoadProfileForm : Form
    {
        private readonly List<RoadModelInfo> _models;
        private CheckBox       _chkPlan, _chkSurface;
        private CheckedListBox _listTargets;
        private Button         _btnAll, _btnNone, _btnOk, _btnCancel;

        public List<RoadModelInfo> SelectedModels { get; private set; }
        public bool ApplyPlan    { get; private set; }
        public bool ApplySurface { get; private set; }

        public LoadProfileForm(List<RoadModelInfo> models, string profilePath, bool hasPlan, bool hasSurface)
        {
            _models = models;
            BuildUI(profilePath, hasPlan, hasSurface);
        }

        private void BuildUI(string profilePath, bool hasPlan, bool hasSurface)
        {
            Text            = "Импортировать стиль";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(MakeBold("Профиль:", 12, y)); y += 22;
            Controls.Add(new Label { Text = Path.GetFileName(profilePath), Left = 16, Top = y, Width = 460, Height = 18, ForeColor = System.Drawing.Color.Gray });
            y += 26;

            Controls.Add(MakeBold("Применить разделы:", 12, y)); y += 24;
            _chkPlan    = new CheckBox { Text = "План",                  Left = 16, Top = y, Width = 460, Height = 20, Checked = hasPlan,    Enabled = hasPlan };    Controls.Add(_chkPlan);    y += 26;
            _chkSurface = new CheckBox { Text = "Проектная поверхность", Left = 16, Top = y, Width = 460, Height = 20, Checked = hasSurface, Enabled = hasSurface }; Controls.Add(_chkSurface); y += 34;

            Controls.Add(MakeBold("Применить к трассам:", 12, y)); y += 24;
            _listTargets = new CheckedListBox { Left = 12, Top = y, Width = 470, Height = 160, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            foreach (var m in _models) _listTargets.Items.Add(m.Name, true);
            Controls.Add(_listTargets); y += 168;

            _btnAll  = new Button { Text = "Выбрать все", Left = 12,  Top = y, Width = 110, Height = 26 };
            _btnNone = new Button { Text = "Снять все",   Left = 130, Top = y, Width = 100, Height = 26 };
            _btnAll.Click  += (s, e) => { for (int i = 0; i < _listTargets.Items.Count; i++) _listTargets.SetItemChecked(i, true);  };
            _btnNone.Click += (s, e) => { for (int i = 0; i < _listTargets.Items.Count; i++) _listTargets.SetItemChecked(i, false); };
            Controls.Add(_btnAll); Controls.Add(_btnNone); y += 36;

            _btnOk = new Button { Text = "Применить", Left = 290, Top = y, Width = 90, Height = 28 };
            _btnOk.Click += (s, e) =>
            {
                if (!_chkPlan.Checked && !_chkSurface.Checked)
                { MessageDlg.Show("Выберите хотя бы один раздел."); return; }
                var selected = new List<RoadModelInfo>();
                for (int i = 0; i < _listTargets.Items.Count; i++)
                    if (_listTargets.GetItemChecked(i)) selected.Add(_models[i]);
                if (selected.Count == 0) { MessageDlg.Show("Выберите хотя бы одну трассу."); return; }
                SelectedModels = selected;
                ApplyPlan      = _chkPlan.Checked;
                ApplySurface   = _chkSurface.Checked;
                DialogResult   = DialogResult.OK;
                Close();
            };
            Controls.Add(_btnOk);

            _btnCancel = new Button { Text = "Отмена", Left = 390, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(_btnCancel);

            Height = y + 68;
            CancelButton = _btnCancel;
        }

        private Label MakeBold(string text, int left, int top) => new Label
        {
            Text = text, Left = left, Top = top, Width = 470, Height = 20,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
        };
    }
}
