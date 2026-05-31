using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Topomatic.Alg.Model;
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
        [cmd("export_xml_settings")]
        public void ExportXmlSettings()
        {
            var models = new List<ExportModelInfo>();

            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var am = pm.LockRead() as AlignmentModel;
                    if (am?.Drawing != null && am.Drawing.LayerStates?.Count > 0)
                    {
                        models.Add(new ExportModelInfo(GetModelName(pm, am), am.Drawing));
                        return false;
                    }

                    var sm = pm.LockRead() as SiteModel;
                    if (sm?.Drawing != null && sm.Drawing.LayerStates?.Count > 0)
                        models.Add(new ExportModelInfo(GetSiteName(pm, sm), sm.Drawing));
                }
                catch { }
                return false;
            });

            if (models.Count == 0)
            {
                MessageDlg.Show("Нет открытых моделей с сохранёнными состояниями слоёв.\n\n" +
                                "Сначала сохраните состояния в диспетчере слоёв.");
                return;
            }

            using (var form = new ExportLayersForm(models))
            {
                if (form.ShowDialog() != DialogResult.OK) return;
                ExportLayerStates(form.SelectedDrawing, form.SelectedPath);
            }
        }

        private void ExportLayerStates(Drawing drawing, string outputPath)
        {
            try
            {
                var stgDoc = new StgDocument();

                var flags = System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance;

                System.Reflection.MethodInfo saveM = null;
                for (var t = drawing.LayerStates.GetType(); t != null; t = t.BaseType)
                    foreach (var m in t.GetMethods(flags))
                        if (m.Name == "SaveToStg" && m.GetParameters().Length >= 1)
                        { saveM = m; break; }

                if (saveM == null)
                {
                    MessageDlg.Show("Ошибка: метод SaveToStg не найден.");
                    return;
                }

                var prms = saveM.GetParameters();
                if (prms.Length == 1)
                    saveM.Invoke(drawing.LayerStates, new object[] { stgDoc.Body });
                else
                    saveM.Invoke(drawing.LayerStates, new object[] { stgDoc.Body, null });

                stgDoc.SaveToFileAsXml(outputPath);

                MessageDlg.Show($"✓ Экспортировано {drawing.LayerStates.Count} состояний слоёв.\n\n{outputPath}");
            }
            catch (Exception ex)
            {
                MessageDlg.Show("Ошибка экспорта:\n" + ex.Message +
                    (ex.InnerException != null ? "\n" + ex.InnerException.Message : ""));
            }
        }
    }

    internal class ExportModelInfo
    {
        public string  Name    { get; }
        public Drawing Drawing { get; }

        public ExportModelInfo(string name, Drawing drawing)
        {
            Name    = name;
            Drawing = drawing;
        }
    }

    internal class ExportLayersForm : Form
    {
        private readonly List<ExportModelInfo> _models;

        private ComboBox _cmbModel;
        private Label    _lblStates;
        private TextBox  _txtPath;
        private Button   _btnBrowse, _btnOk, _btnCancel;

        public Drawing SelectedDrawing { get; private set; }
        public string  SelectedPath   { get; private set; }

        public ExportLayersForm(List<ExportModelInfo> models)
        {
            _models = models;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "Экспорт состояний слоёв";
            Width           = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(new Label
            {
                Text = "Источник (модель):",
                Left = 12, Top = y, Width = 470, Height = 18,
                Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
            });
            y += 22;

            _cmbModel = new ComboBox
            {
                Left = 12, Top = y, Width = 470, Height = 24,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var m in _models)
                _cmbModel.Items.Add(m.Name);
            _cmbModel.SelectedIndex = 0;
            _cmbModel.SelectedIndexChanged += (s, e) => UpdateStatesLabel();
            Controls.Add(_cmbModel);
            y += 30;

            _lblStates = new Label
            {
                Left = 12, Top = y, Width = 470, Height = 18,
                ForeColor = System.Drawing.Color.Gray
            };
            Controls.Add(_lblStates);
            y += 26;

            Controls.Add(new Label
            {
                Text = "Сохранить в файл:",
                Left = 12, Top = y, Width = 470, Height = 18,
                Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
            });
            y += 22;

            _txtPath = new TextBox
            {
                Left = 12, Top = y, Width = 380, Height = 24,
                ReadOnly = true
            };
            Controls.Add(_txtPath);

            _btnBrowse = new Button { Text = "...", Left = 398, Top = y, Width = 84, Height = 24 };
            _btnBrowse.Click += OnBrowse;
            Controls.Add(_btnBrowse);
            y += 36;

            _btnOk = new Button { Text = "Экспорт", Left = 290, Top = y, Width = 90, Height = 28 };
            _btnOk.Click += OnOk;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "Отмена", Left = 390, Top = y, Width = 90, Height = 28,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_btnCancel);

            Height       = y + 68;
            CancelButton = _btnCancel;

            UpdateStatesLabel();
        }

        private void UpdateStatesLabel()
        {
            if (_cmbModel.SelectedIndex < 0) return;
            var m     = _models[_cmbModel.SelectedIndex];
            var names = new List<string>();
            foreach (DwgLayersState s in m.Drawing.LayerStates)
                names.Add(s.Name);
            _lblStates.Text = $"Состояний: {m.Drawing.LayerStates.Count}  —  " +
                              string.Join(", ", names);
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title           = "Сохранить состояния слоёв";
                dlg.Filter          = "XML-файлы (*.xml)|*.xml";
                dlg.DefaultExt      = "xml";
                dlg.FileName        = _models[_cmbModel.SelectedIndex].Name + "_layers";
                dlg.OverwritePrompt = true;

                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtPath.Text = dlg.FileName;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_txtPath.Text))
            {
                MessageDlg.Show("Укажите путь для сохранения файла.");
                return;
            }

            SelectedDrawing = _models[_cmbModel.SelectedIndex].Drawing;
            SelectedPath    = _txtPath.Text;
            DialogResult    = DialogResult.OK;
            Close();
        }
    }
}
