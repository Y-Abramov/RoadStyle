using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.Xml;
using Topomatic.Alg.Model;
using Topomatic.Alg.Road;
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.Dwg;
using Topomatic.Sites.Core;
using Topomatic.Stg;

namespace RoadStyle
{
    public partial class RoadStylePlugin : PluginInitializator
    {
        [cmd("apply_xml_settings")]
        public void ApplyXmlSettings()
        {
            string xmlPath;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title  = "Выберите файл конфигурации слоёв";
                dlg.Filter = "XML-файлы (*.xml)|*.xml|Все файлы (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                xmlPath = dlg.FileName;
            }

            var xml = new XmlDocument();
            try { xml.Load(xmlPath); }
            catch (Exception ex)
            {
                MessageDlg.Show("Ошибка загрузки файла:\n" + ex.Message);
                return;
            }

            var layerStates = ParseLayerStates(xml);
            if (layerStates.Count == 0)
            {
                MessageDlg.Show("В файле не найдено состояний слоёв (LayersStates).");
                return;
            }

            var models = new List<RoadModelInfo>();
            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var am = pm.LockRead() as AlignmentModel;
                    if (am?.Drawing != null)
                    {
                        models.Add(new RoadModelInfo(GetModelName(pm, am), pm, am.Drawing, ModelType.Road));
                        return false;
                    }

                    var sm = pm.LockRead() as SiteModel;
                    if (sm?.Drawing != null)
                        models.Add(new RoadModelInfo(GetSiteName(pm, sm), pm, sm.Drawing, ModelType.Site));
                }
                catch { }
                return false;
            });

            if (models.Count == 0)
            {
                MessageDlg.Show("Не найдено открытых моделей дорог или площадок.");
                return;
            }

            using (var form = new ApplySettingsForm(models, layerStates, xmlPath))
                form.ShowDialog();
        }

        [cmd("copy_road_settings")]
        public void CopyRoadSettings()
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

            if (models.Count < 2)
            {
                MessageDlg.Show("Нужно минимум 2 открытые трассы.");
                return;
            }

            using (var form = new CopySettingsForm(models))
                form.ShowDialog();
        }

        private string GetModelName(IProjectModel pm, AlignmentModel am)
        {
            try
            {
                var prop = am.Alignment.GetType().GetProperty("Name");
                if (prop != null)
                {
                    var val = prop.GetValue(am.Alignment) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }

            try
            {
                foreach (var propName in new[] { "Name", "Caption", "URI", "DefaultFileName" })
                {
                    var prop = pm.GetType().GetProperty(propName);
                    if (prop == null) continue;
                    var val = prop.GetValue(pm) as string;
                    if (!string.IsNullOrEmpty(val) && val != "Road")
                    {
                        var dot = val.LastIndexOf('.');
                        if (dot > 0) val = val.Substring(0, dot);
                        return val;
                    }
                }
            }
            catch { }

            return am.Alignment?.Alias ?? "Трасса";
        }

        private string GetSiteName(IProjectModel pm, SiteModel sm)
        {
            try
            {
                foreach (var propName in new[] { "Name", "Caption", "URI", "DefaultFileName" })
                {
                    var prop = pm.GetType().GetProperty(propName);
                    if (prop == null) continue;
                    var val = prop.GetValue(pm) as string;
                    if (!string.IsNullOrEmpty(val) && val != "Site")
                    {
                        var dot = val.LastIndexOf('.');
                        if (dot > 0) val = val.Substring(0, dot);
                        return val;
                    }
                }
            }
            catch { }
            return "Площадка";
        }

        private List<LayerStateData> ParseLayerStates(XmlDocument xml)
        {
            var result = new List<LayerStateData>();
            var states = xml.SelectNodes("/Stg/Body/LayersStates/State");
            if (states == null) return result;

            foreach (XmlNode stateNode in states)
            {
                var stateName = stateNode.Attributes?["Name"]?.Value;
                if (string.IsNullOrEmpty(stateName)) continue;

                var layers = new List<LayerData>();
                foreach (XmlNode item in stateNode.SelectNodes("Layers/Item"))
                {
                    var layerName = item.Attributes?["Name"]?.Value;
                    if (string.IsNullOrEmpty(layerName)) continue;

                    var ld = new LayerData { Name = layerName };

                    var vis = item.Attributes?["Visible"]?.Value;
                    if (!string.IsNullOrEmpty(vis))
                        ld.Visible = !vis.Equals("false", StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(item.Attributes?["Color"]?.Value) &&
                        short.TryParse(item.Attributes["Color"].Value, out short c))
                        ld.Color = c;

                    if (!string.IsNullOrEmpty(item.Attributes?["Lineweight"]?.Value) &&
                        int.TryParse(item.Attributes["Lineweight"].Value, out int lw))
                        ld.Lineweight = lw;

                    layers.Add(ld);
                }

                result.Add(new LayerStateData { Name = stateName, Layers = layers });
            }
            return result;
        }
    }

    // ── Модели данных ───────────────────────────────────────────

    internal class LayerData
    {
        public string Name       { get; set; }
        public bool   Visible    { get; set; } = true;
        public short? Color      { get; set; }
        public int?   Lineweight { get; set; }
    }

    internal class LayerStateData
    {
        public string          Name   { get; set; }
        public List<LayerData> Layers { get; set; }
    }

    internal enum ModelType { Road, Site }

    internal class RoadModelInfo
    {
        public string        Name         { get; }
        public IProjectModel ProjectModel { get; }
        public Drawing       Drawing      { get; }
        public ModelType     ModelType    { get; }

        public RoadModelInfo(string name, IProjectModel pm, Drawing drawing, ModelType type)
        {
            Name         = name;
            ProjectModel = pm;
            Drawing      = drawing;
            ModelType    = type;
        }
    }

    // ── Диалог импорта слоёв XML ────────────────────────────────

    internal class ApplySettingsForm : Form
    {
        private readonly List<RoadModelInfo>  _models;
        private readonly List<LayerStateData> _states;
        private readonly string               _xmlPath;

        private CheckedListBox _listRoads;
        private CheckedListBox _listSites;
        private ComboBox       _cmbState;
        private Button         _btnAll, _btnNone, _btnApply, _btnClose;
        private Label          _lblResult;
        private TabControl     _tabs;

        public ApplySettingsForm(List<RoadModelInfo> models, List<LayerStateData> states, string xmlPath)
        {
            _models  = models;
            _states  = states;
            _xmlPath = xmlPath;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "Применить конфигурацию слоёв";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(new Label
            {
                Text = "Конфигурация слоёв:",
                Left = 12, Top = y, Width = 150, Height = 20,
                Font = new Font(Font, FontStyle.Bold)
            });
            y += 22;

            _cmbState = new ComboBox
            {
                Left = 12, Top = y, Width = 470, Height = 24,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var s in _states)
                _cmbState.Items.Add(s.Name);
            if (_cmbState.Items.Count > 0)
                _cmbState.SelectedIndex = 0;
            Controls.Add(_cmbState);
            y += 36;

            _tabs = new TabControl { Left = 12, Top = y, Width = 470, Height = 260 };

            var tabRoads = new TabPage("Дороги");
            _listRoads = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None };
            foreach (var m in _models)
                if (m.ModelType == ModelType.Road)
                    _listRoads.Items.Add(m.Name, true);
            tabRoads.Controls.Add(_listRoads);

            var tabSites = new TabPage("Площадки");
            _listSites = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None };
            foreach (var m in _models)
                if (m.ModelType == ModelType.Site)
                    _listSites.Items.Add(m.Name, true);
            tabSites.Controls.Add(_listSites);

            _tabs.TabPages.Add(tabRoads);
            _tabs.TabPages.Add(tabSites);
            Controls.Add(_tabs);
            y += 268;

            _btnAll = new Button { Text = "Выбрать все", Left = 12, Top = y, Width = 110, Height = 26 };
            _btnAll.Click += (s, e) => { var l = ActiveList; for (int i = 0; i < l.Items.Count; i++) l.SetItemChecked(i, true); };
            Controls.Add(_btnAll);

            _btnNone = new Button { Text = "Снять все", Left = 130, Top = y, Width = 100, Height = 26 };
            _btnNone.Click += (s, e) => { var l = ActiveList; for (int i = 0; i < l.Items.Count; i++) l.SetItemChecked(i, false); };
            Controls.Add(_btnNone);
            y += 36;

            _lblResult = new Label { Left = 12, Top = y, Width = 470, Height = 22, ForeColor = Color.Green };
            Controls.Add(_lblResult);
            y += 28;

            _btnApply = new Button { Text = "Применить", Left = 290, Top = y, Width = 90, Height = 28 };
            _btnApply.Click += OnApply;
            Controls.Add(_btnApply);

            _btnClose = new Button { Text = "Закрыть", Left = 390, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(_btnClose);

            Height        = y + 68;
            AcceptButton  = _btnApply;
            CancelButton  = _btnClose;
        }

        private CheckedListBox ActiveList =>
            _tabs.SelectedTab?.Text == "Площадки" ? _listSites : _listRoads;

        private void OnApply(object sender, EventArgs e)
        {
            if (_cmbState.SelectedIndex < 0)
            {
                MessageDlg.Show("Выберите конфигурацию слоёв.");
                return;
            }

            var state   = _states[_cmbState.SelectedIndex];
            int applied = 0;
            var errors  = new List<string>();

            var roads    = _models.FindAll(m => m.ModelType == ModelType.Road);
            var sites    = _models.FindAll(m => m.ModelType == ModelType.Site);
            var selected = new List<RoadModelInfo>();

            for (int i = 0; i < _listRoads.Items.Count; i++)
                if (_listRoads.GetItemChecked(i)) selected.Add(roads[i]);
            for (int i = 0; i < _listSites.Items.Count; i++)
                if (_listSites.GetItemChecked(i)) selected.Add(sites[i]);

            foreach (var info in selected)
            {
                try
                {
                    info.ProjectModel.LockWrite();
                    try
                    {
                        ApplyLayerState(info.Drawing, state);
                        info.ProjectModel.Modified = true;
                        applied++;
                    }
                    finally { info.ProjectModel.UnlockWrite(); }
                }
                catch (Exception ex) { errors.Add(info.Name + ": " + ex.Message); }
            }

            if (errors.Count > 0)
                MessageDlg.Show("Применено: " + applied + "\nОшибки:\n" + string.Join("\n", errors));
            else
                _lblResult.Text = "✓ Применено к " + applied + " трасс(е).";
        }

        private void ApplyLayerState(Drawing drawing, LayerStateData state)
        {
            drawing.BeginUpdate();
            try
            {
                foreach (var ld in state.Layers)
                {
                    var layer = drawing.Layers[ld.Name];
                    if (layer == null) continue;

                    layer.Visible = ld.Visible;

                    if (ld.Lineweight.HasValue)
                        layer.Lineweight = (Lineweight)ld.Lineweight.Value;

                    if (ld.Color.HasValue)
                    {
                        try
                        {
                            var fromMethod = layer.Color.GetType().GetMethod("FromCompressValue",
                                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                            if (fromMethod != null)
                                layer.Color = (dynamic)fromMethod.Invoke(null, new object[] { ld.Color.Value });
                        }
                        catch { }
                    }
                }
            }
            finally { drawing.EndUpdate(); }

            // Импортируем именованную конфигурацию через StgDocument если её ещё нет
            if (drawing.LayerStates[state.Name] == null)
            {
                try
                {
                    var stgDoc = new StgDocument();
                    stgDoc.LoadFromFileAsXml(_xmlPath);

                    var loadMethod = drawing.LayerStates.GetType().GetMethod("LoadFromStg",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    loadMethod?.Invoke(drawing.LayerStates, new object[] { stgDoc.Body, null });
                }
                catch { }
            }
        }
    }

    // ── Диалог копирования настроек дорог ──────────────────────

    internal class CopySettingsForm : Form
    {
        private readonly List<RoadModelInfo> _models;

        private ComboBox       _cmbSource;
        private CheckedListBox _listTargets;
        private TextBox        _txtSearch;
        private CheckBox       _chkSurface;
        private CheckBox       _chkPlan;
        private CheckBox       _chkDtm;
        private Button         _btnApply, _btnClose, _btnAssignDtm;
        private Label          _lblResult;

        private List<string> _selectedDtmPaths = null;

        public CopySettingsForm(List<RoadModelInfo> models)
        {
            _models = models;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "Массовые настройки дорог";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(new Label { Text = "Источник настроек:", Left = 12, Top = y, Width = 470, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            y += 24;

            _cmbSource = new ComboBox { Left = 12, Top = y, Width = 470, Height = 24, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in _models) _cmbSource.Items.Add(m.Name);
            if (_cmbSource.Items.Count > 0) _cmbSource.SelectedIndex = 0;
            Controls.Add(_cmbSource);
            y += 34;

            Controls.Add(new Label { Text = "Разделы для копирования:", Left = 12, Top = y, Width = 470, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            y += 24;

            _chkSurface = new CheckBox { Text = "Проектная поверхность (горизонтали, точки, структурные линии, рёбра, уклоны)", Left = 16, Top = y, Width = 460, Height = 20, Checked = true };
            Controls.Add(_chkSurface); y += 26;

            _chkPlan = new CheckBox { Text = "План (линия плана, пикетаж, километраж, подписи вершин)", Left = 16, Top = y, Width = 460, Height = 20, Checked = true };
            Controls.Add(_chkPlan); y += 26;

            _chkDtm = new CheckBox { Text = "ЦММ (назначить поверхности)", Left = 16, Top = y, Width = 340, Height = 20, Checked = false };
            Controls.Add(_chkDtm);

            _btnAssignDtm = new Button { Text = "Выбрать ЦММ...", Left = 360, Top = y - 2, Width = 120, Height = 24 };
            _btnAssignDtm.Click += OnAssignDtm;
            Controls.Add(_btnAssignDtm);
            y += 34;

            Controls.Add(new Label { Text = "Применить к трассам:", Left = 12, Top = y, Width = 200, Height = 20, Font = new Font(Font, FontStyle.Bold) });

            _txtSearch = new TextBox { Left = 220, Top = y - 2, Width = 262, Height = 22,
                Text = "Поиск по имени...", ForeColor = System.Drawing.Color.Gray };
            _txtSearch.GotFocus  += (s, e) => { if (_txtSearch.ForeColor == System.Drawing.Color.Gray) { _txtSearch.Text = ""; _txtSearch.ForeColor = System.Drawing.Color.Black; } };
            _txtSearch.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_txtSearch.Text)) { _txtSearch.Text = "Поиск по имени..."; _txtSearch.ForeColor = System.Drawing.Color.Gray; } };
            Controls.Add(_txtSearch);
            y += 24;

            _listTargets = new CheckedListBox { Left = 12, Top = y, Width = 470, Height = 160, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            foreach (var m in _models) _listTargets.Items.Add(m.Name, true);
            Controls.Add(_listTargets);
            y += 168;

            // Фильтр по имени
            _txtSearch.TextChanged += (s, e) =>
            {
                var raw    = _txtSearch.ForeColor == System.Drawing.Color.Gray ? "" : _txtSearch.Text;
                var filter = raw.Trim().ToLower();
                var checked_ = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < _listTargets.Items.Count; i++)
                    if (_listTargets.GetItemChecked(i))
                        checked_.Add(_listTargets.Items[i].ToString());
                _listTargets.Items.Clear();
                foreach (var m in _models)
                    if (string.IsNullOrEmpty(filter) || m.Name.ToLower().Contains(filter))
                        _listTargets.Items.Add(m.Name, checked_.Contains(m.Name));
            };

            var _btnAll2  = new Button { Text = "Выбрать все", Left = 12,  Top = y, Width = 110, Height = 26 };
            var _btnNone2 = new Button { Text = "Снять все",   Left = 130, Top = y, Width = 100, Height = 26 };
            _btnAll2.Click  += (s, e) => { for (int i = 0; i < _listTargets.Items.Count; i++) _listTargets.SetItemChecked(i, true);  };
            _btnNone2.Click += (s, e) => { for (int i = 0; i < _listTargets.Items.Count; i++) _listTargets.SetItemChecked(i, false); };
            Controls.Add(_btnAll2); Controls.Add(_btnNone2);

            var _btnRebuild = new Button { Text = "Перестроить поверхность", Left = 240, Top = y, Width = 180, Height = 26 };
            _btnRebuild.Click += OnRebuildSurface;
            Controls.Add(_btnRebuild);
            y += 34;

            _lblResult = new Label { Left = 12, Top = y, Width = 470, Height = 22, ForeColor = Color.Green };
            Controls.Add(_lblResult);
            y += 28;

            _btnApply = new Button { Text = "Применить", Left = 290, Top = y, Width = 90, Height = 28 };
            _btnApply.Click += OnApply;
            Controls.Add(_btnApply);

            _btnClose = new Button { Text = "Закрыть", Left = 390, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(_btnClose);

            Height       = y + 68;
            AcceptButton = _btnApply;
            CancelButton = _btnClose;
        }

        private void OnRebuildSurface(object sender, EventArgs e)
        {
            // Собираем выбранные трассы
            var selected = new System.Collections.Generic.List<RoadModelInfo>();
            for (int i = 0; i < _listTargets.Items.Count; i++)
                if (_listTargets.GetItemChecked(i))
                    selected.Add(_models[i]);

            if (selected.Count == 0)
            {
                Topomatic.Controls.Dialogs.MessageDlg.Show("Выберите хотя бы одну трассу.");
                return;
            }

            int rebuilt = 0;
            var errors  = new System.Collections.Generic.List<string>();

            foreach (var target in selected)
            {
                try
                {
                    // Активируем модель и вызываем TLC команды
                    // rebuild_design_surface — перестроить проектную поверхность
                    // project_slopes        — перерисовать откосы
                    PluginCoreOps.FilterOpenedModels(pm =>
                    {
                        if (pm == target.ProjectModel)
                        {
                            try
                            {
                                Topomatic.ApplicationPlatform.ApplicationHost.Current.Plugins
                                    .Execute("rebuild_design_surface", new object[] { pm });
                                Topomatic.ApplicationPlatform.ApplicationHost.Current.Plugins
                                    .Execute("project_slopes", new object[] { pm });
                                rebuilt++;
                            }
                            catch { }
                            return true; // останавливаем перебор
                        }
                        return false;
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(target.Name + ": " + ex.Message);
                }
            }

            _lblResult.Text = "✓ Перестроено: " + rebuilt + " трасс(е).";
            if (errors.Count > 0)
                Topomatic.Controls.Dialogs.MessageDlg.Show(
                    "Перестроено: " + rebuilt + "\nОшибки:\n" + string.Join("\n", errors));
        }

        private void OnAssignDtm(object sender, EventArgs e)
        {
            var dtmModels = new List<KeyValuePair<string, string>>();

            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var mtype = pm.GetType().GetProperty("ModelType")?.GetValue(pm)?.ToString() ?? "";
                    if (!mtype.Equals("dtm", StringComparison.OrdinalIgnoreCase)) return false;

                    var name   = pm.GetType().GetProperty("Name")?.GetValue(pm)?.ToString() ?? "?";
                    var uriObj = pm.GetType().GetProperty("Uri")?.GetValue(pm);
                    if (uriObj == null) return false;

                    var uriStr = uriObj.GetType().GetProperty("AsFilePath")?.GetValue(uriObj)?.ToString()
                              ?? uriObj.GetType().GetProperty("LocalPath")?.GetValue(uriObj)?.ToString()
                              ?? uriObj.ToString();

                    if (!string.IsNullOrEmpty(uriStr))
                        dtmModels.Add(new KeyValuePair<string, string>(name, uriStr));
                }
                catch { }
                return false;
            });

            if (dtmModels.Count == 0)
            {
                MessageDlg.Show("Не найдено открытых поверхностей ЦММ (файлы .sfcx).");
                return;
            }

            using (var dlg = new DtmSelectForm(dtmModels, _selectedDtmPaths))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _selectedDtmPaths = dlg.SelectedPaths;
                    _chkDtm.Checked   = _selectedDtmPaths?.Count > 0;
                    _chkDtm.Text      = _selectedDtmPaths?.Count > 0
                        ? $"ЦММ — выбрано {_selectedDtmPaths.Count} поверхн."
                        : "ЦММ (назначить поверхности)";
                }
            }
        }

        private void OnApply(object sender, EventArgs e)
        {
            if (_cmbSource.SelectedIndex < 0)
            {
                MessageDlg.Show("Выберите эталонную дорогу.");
                return;
            }

            var source   = _models[_cmbSource.SelectedIndex];
            var sourceAm = source.ProjectModel.LockRead() as AlignmentModel;

            if (sourceAm == null)
            {
                MessageDlg.Show("Не удалось получить настройки эталонной дороги.");
                return;
            }

            int applied = 0;
            var errors  = new List<string>();

            for (int i = 0; i < _listTargets.Items.Count; i++)
            {
                if (!_listTargets.GetItemChecked(i)) continue;
                var target = _models[i];
                if (target == source) continue;

                try
                {
                    target.ProjectModel.LockWrite();
                    try
                    {
                        var am = target.ProjectModel.LockRead() as AlignmentModel;
                        if (am == null) continue;

                        if (_chkSurface.Checked && sourceAm.Surface != null && am.Surface != null)
                        {
                            try
                            {
                                var srcStyle = GetProp(sourceAm.Surface, "Style");
                                var tgtStyle = GetProp(am.Surface, "Style");
                                if (srcStyle != null && tgtStyle != null)
                                {
                                    foreach (var prop in new[] { "HorizontalsStyle", "PointsStyle",
                                        "StructureLinesStyle", "InclinationsStyle", "TrianglesStyle" })
                                    {
                                        var src = GetProp(srcStyle, prop);
                                        var tgt = GetProp(tgtStyle, prop);
                                        if (src == null || tgt == null) continue;
                                        CopyObjectSettings(src, tgt);
                                        foreach (var p in new[] { "Visible", "ThickVisible", "ThinVisible", "TextVisible", "AdditionalVisible" })
                                            CopyProperty(src, tgt, p);
                                    }
                                }
                                foreach (var p in new[] { "Visible", "RibsVisible", "TrianglesVisible" })
                                    CopyProperty(sourceAm.Surface, am.Surface, p);
                            }
                            catch { }
                        }

                        if (_chkPlan.Checked && sourceAm.Alignment != null && am.Alignment != null)
                        {
                            try
                            {
                                CopyObjectSettings((object)((dynamic)sourceAm.Alignment).Style,
                                                   (object)((dynamic)am.Alignment).Style);
                            }
                            catch { }
                        }

                        if (_chkDtm.Checked && _selectedDtmPaths?.Count > 0)
                        {
                            try { AssignDtmPaths(am, _selectedDtmPaths, target.ProjectModel); }
                            catch { }
                        }

                        target.ProjectModel.Modified = true;
                        applied++;
                    }
                    finally { target.ProjectModel.UnlockWrite(); }
                }
                catch (Exception ex) { errors.Add(target.Name + ": " + ex.Message); }
            }

            if (errors.Count > 0)
                MessageDlg.Show("Применено: " + applied + "\nОшибки:\n" + string.Join("\n", errors));
            else
                _lblResult.Text = "✓ Применено к " + applied + " трасс(е).";
        }

        private void AssignDtmPaths(AlignmentModel am, List<string> absPaths, IProjectModel targetModel)
        {
            var tgtPaths = GetProp(am.Alignment, "EgSurfaceRelativePaths");
            if (tgtPaths == null) return;

            var flags     = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var clearM    = FindMethod(tgtPaths.GetType(), "Clear", flags);
            var addM      = FindAddString(tgtPaths.GetType(), flags);
            if (clearM == null || addM == null) return;

            string targetFolder = GetModelFolder(targetModel);
            var relPaths = new List<string>();
            foreach (var abs in absPaths)
            {
                var rel = string.IsNullOrEmpty(targetFolder) ? abs : MakeRelativePath(targetFolder, abs);
                relPaths.Add(rel.Replace('\\', '/'));
            }

            // BeginUpdate — предпочитаем перегрузку с string
            var algType = am.Alignment.GetType();
            System.Reflection.MethodInfo beginM = null, endM = null;
            var t = algType;
            while (t != null)
            {
                if (beginM == null)
                    foreach (var m in t.GetMethods(flags))
                        if (m.Name == "BeginUpdate")
                        {
                            var p = m.GetParameters();
                            if (p.Length == 1 && p[0].ParameterType == typeof(string)) { beginM = m; break; }
                            if (p.Length == 0 && beginM == null) beginM = m;
                        }
                if (endM == null)
                    foreach (var m in t.GetMethods(flags))
                        if (m.Name == "EndUpdate" && m.GetParameters().Length == 0) { endM = m; break; }
                if (beginM != null && endM != null) break;
                t = t.BaseType;
            }

            // Временно отключаем DynamicSurface чтобы EndUpdate не триггерил перестроение
            var dynProp = am.Alignment.GetType().GetProperty("DynamicSurface",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            bool wasDynamic = false;
            if (dynProp != null && dynProp.CanRead && dynProp.CanWrite)
            {
                wasDynamic = (bool)(dynProp.GetValue(am.Alignment) ?? false);
                if (wasDynamic) dynProp.SetValue(am.Alignment, false);
            }

            var bArgs = beginM?.GetParameters().Length == 0 ? null : new object[] { "Назначение ЦММ" };
            beginM?.Invoke(am.Alignment, bArgs);
            try
            {
                clearM.Invoke(tgtPaths, null);
                foreach (var rel in relPaths)
                    addM.Invoke(tgtPaths, new object[] { rel });
            }
            finally
            {
                endM?.Invoke(am.Alignment, null);
                // Восстанавливаем DynamicSurface
                if (wasDynamic && dynProp != null) dynProp.SetValue(am.Alignment, true);
            }
        }

        private string GetModelFolder(IProjectModel pm)
        {
            try
            {
                var uriObj = pm.GetType().GetProperty("Uri")?.GetValue(pm);
                if (uriObj == null) return null;
                var path = uriObj.GetType().GetProperty("AsFilePath")?.GetValue(uriObj)?.ToString()
                        ?? uriObj.GetType().GetProperty("LocalPath")?.GetValue(uriObj)?.ToString();
                return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path);
            }
            catch { return null; }
        }

        private string MakeRelativePath(string baseDir, string targetPath)
        {
            try
            {
                var baseUri   = new Uri(baseDir.TrimEnd('\\', '/') + "/");
                var targetUri = new Uri(targetPath);
                return Uri.UnescapeDataString(
                    baseUri.MakeRelativeUri(targetUri).ToString()
                           .Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
            catch { return targetPath; }
        }

        private object GetProp(object obj, string propName)
        {
            if (obj == null) return null;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var type  = obj.GetType();
            while (type != null)
            {
                var prop = type.GetProperty(propName, flags);
                if (prop != null) return prop.GetValue(obj);
                type = type.BaseType;
            }
            return null;
        }

        private void CopyProperty(object src, object tgt, string propName)
        {
            if (src == null || tgt == null) return;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            try
            {
                System.Reflection.MethodInfo getM = null, setM = null;
                for (var t = src.GetType(); t != null && getM == null; t = t.BaseType)
                    getM = t.GetMethod("get_" + propName, flags);
                for (var t = tgt.GetType(); t != null && setM == null; t = t.BaseType)
                    setM = t.GetMethod("set_" + propName, flags);
                var val = getM?.Invoke(src, null);
                if (val != null) setM?.Invoke(tgt, new[] { val });
            }
            catch { }
        }

        private void CopyObjectSettings(object sourceObj, object targetObj)
        {
            if (sourceObj == null || targetObj == null) return;
            var flags   = System.Reflection.BindingFlags.Public |
                          System.Reflection.BindingFlags.NonPublic |
                          System.Reflection.BindingFlags.Instance;
            var tempDoc = new StgDocument();

            // Сохраняем маппинг слоёв целевого объекта ДО загрузки стиля
            var layerBackup = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var lp in new[] { "LayerName", "DwgLayer", "Layer" })
            {
                try
                {
                    for (var t = targetObj.GetType(); t != null; t = t.BaseType)
                    {
                        var p = t.GetProperty(lp, flags);
                        if (p != null && p.CanRead) { layerBackup[lp] = p.GetValue(targetObj); break; }
                    }
                }
                catch { }
            }

            // Копируем стиль
            System.Reflection.MethodInfo saveM = null, loadM = null;
            for (var t = sourceObj.GetType(); t != null && saveM == null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "SaveToStg" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(StgNode))
                    { saveM = m; break; }

            if (saveM == null) return;
            saveM.Invoke(sourceObj, new object[] { tempDoc.Body });

            for (var t = targetObj.GetType(); t != null && loadM == null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "LoadFromStg" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(StgNode))
                    { loadM = m; break; }

            loadM?.Invoke(targetObj, new object[] { tempDoc.Body });

            // Восстанавливаем маппинг слоёв целевого объекта
            foreach (var kv in layerBackup)
            {
                if (kv.Value == null) continue;
                try
                {
                    for (var t = targetObj.GetType(); t != null; t = t.BaseType)
                    {
                        var p = t.GetProperty(kv.Key, flags);
                        if (p != null && p.CanWrite) { p.SetValue(targetObj, kv.Value); break; }
                    }
                }
                catch { }
            }
        }

        private System.Reflection.MethodInfo FindMethod(Type type, string name, System.Reflection.BindingFlags flags)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name, flags);
                if (m != null) return m;
            }
            return null;
        }

        private System.Reflection.MethodInfo FindAddString(Type type, System.Reflection.BindingFlags flags)
        {
            for (var t = type; t != null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "Add" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(string))
                        return m;

            for (var t = type; t != null; t = t.BaseType)
                foreach (var m in t.GetMethods(flags))
                    if (m.Name == "Add" && m.GetParameters().Length == 1)
                        return m;
            return null;
        }
    }

    // ── Диалог выбора поверхностей ЦММ ─────────────────────────

    internal class DtmSelectForm : Form
    {
        private readonly List<KeyValuePair<string, string>> _surfaces;
        private CheckedListBox _list;

        public List<string> SelectedPaths { get; private set; }

        public DtmSelectForm(List<KeyValuePair<string, string>> surfaces, List<string> preselected)
        {
            _surfaces = surfaces;
            BuildUI(preselected);
        }

        private void BuildUI(List<string> preselected)
        {
            Text            = "Выберите поверхности ЦММ";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;
            Controls.Add(new Label { Text = "Поверхности будут назначены всем выбранным трассам:", Left = 12, Top = y, Width = 460, Height = 18 });
            y += 24;

            _list = new CheckedListBox { Left = 12, Top = y, Width = 460, Height = 220, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            foreach (var kv in _surfaces)
            {
                bool check = preselected != null && preselected.Any(p =>
                    string.Equals(p, kv.Value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(System.IO.Path.GetFileName(p), System.IO.Path.GetFileName(kv.Value), StringComparison.OrdinalIgnoreCase));
                _list.Items.Add(kv.Key, check);
            }
            Controls.Add(_list);
            y += 228;

            var btnOk = new Button { Text = "ОК", Left = 280, Top = y, Width = 90, Height = 28 };
            btnOk.Click += (s, e) =>
            {
                SelectedPaths = new List<string>();
                for (int i = 0; i < _list.Items.Count; i++)
                    if (_list.GetItemChecked(i))
                        SelectedPaths.Add(_surfaces[i].Value);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnOk);

            var btnCancel = new Button { Text = "Отмена", Left = 378, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);

            Height       = y + 68;
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}
