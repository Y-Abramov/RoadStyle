// QuickLayerStateCommand.cs
// Кнопка "Применить конфигурацию" — диалог выбора состояния слоёв
// из всех открытых моделей с применением через DwgLayersState.Restore()

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

namespace RoadStyle
{
    public partial class RoadStylePlugin : PluginInitializator
    {
        [cmd("apply_layer_state")]
        public void ApplyLayerState()
        {
            // Собираем все уникальные состояния из всех открытых моделей
            var stateNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var drawing = GetModelDrawing(pm);
                    if (drawing == null) return false;

                    foreach (DwgLayersState s in drawing.LayerStates)
                        if (!string.IsNullOrEmpty(s.Name) && seen.Add(s.Name))
                            stateNames.Add(s.Name);
                }
                catch { }
                return false;
            });

            if (stateNames.Count == 0)
            {
                MessageDlg.Show("Нет доступных конфигураций слоёв.\n\n" +
                                "Сначала сохраните состояния в диспетчере слоёв " +
                                "или импортируйте их через «Импорт слоёв XML».");
                return;
            }

            using (var form = new ApplyLayerStateForm(stateNames))
            {
                if (form.ShowDialog() != DialogResult.OK) return;

                var stateName = form.SelectedState;
                int applied = 0;
                int skipped = 0;

                PluginCoreOps.FilterOpenedModels(pm =>
                {
                    try
                    {
                        var drawing = GetModelDrawing(pm);
                        if (drawing == null) return false;

                        var state = drawing.LayerStates[stateName];
                        if (state == null) { skipped++; return false; }

                        pm.LockWrite();
                        try
                        {
                            state.Restore();
                            pm.Modified = true;
                            applied++;
                        }
                        finally { pm.UnlockWrite(); }
                    }
                    catch { }
                    return false;
                });

                var msg = $"✓ Конфигурация «{stateName}» применена к {applied} модел(и/ей).";
                if (skipped > 0)
                    msg += $"\n{skipped} модел(и/ей) пропущено — конфигурация не найдена.";
                MessageDlg.Show(msg);
            }
        }

        private Drawing GetModelDrawing(IProjectModel pm)
        {
            try
            {
                var am = pm.LockRead() as AlignmentModel;
                if (am?.Drawing != null) return am.Drawing;

                var sm = pm.LockRead() as SiteModel;
                if (sm?.Drawing != null) return sm.Drawing;
            }
            catch { }
            return null;
        }
        [cmd("about_roadstyle")]
        public void AboutRoadStyle()
        {
            MessageDlg.Show(
                "RoadStyle v1.0.0\n\n" +
                "Расширение для управления конфигурациями слоёв и стилями " +
                "автомобильных дорог в Topomatic Robur.\n\n" +
                "Функции:\n" +
                "  • Применение сохранённых конфигураций слоёв к выбранным объектам проекта.\n" +
                "  • Импорт конфигураций слоёв из XML-файлов.\n" +
                "  • Экспорт конфигураций слоёв в XML для повторного использования и обмена между проектами.\n" +
                "  • Копирование параметров оформления и отображения между автомобильными дорогами.\n" +
                "  • Экспорт пользовательских настроек во внешний файл.\n" +
                "  • Импорт ранее сохранённых настроек в текущий проект.\n\n" +
                "Назначение: Повышение скорости настройки объектов и обеспечение единых " +
                "стандартов оформления проектной документации.\n\n" +
                "Автор: Yaroslav I. Abramov");
        }
    }

    // ── Диалог выбора конфигурации слоёв ───────────────────────

    internal class ApplyLayerStateForm : Form
    {
        private readonly List<string> _states;
        private ListBox _list;
        private Button  _btnOk, _btnCancel;

        public string SelectedState { get; private set; }

        public ApplyLayerStateForm(List<string> states)
        {
            _states = states;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "Применить конфигурацию слоёв";
            Width           = 380;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            int y = 12;

            Controls.Add(new Label
            {
                Text   = "Выберите конфигурацию:",
                Left   = 12, Top = y, Width = 350, Height = 18,
                Font   = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
            });
            y += 22;

            _list = new ListBox
            {
                Left             = 12, Top = y, Width = 350, Height = 200,
                BorderStyle      = BorderStyle.FixedSingle,
                SelectionMode    = SelectionMode.One,
                IntegralHeight   = false
            };
            foreach (var s in _states)
                _list.Items.Add(s);
            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;

            // Двойной клик — сразу применяем
            _list.DoubleClick += (s, e) => OnOk();
            Controls.Add(_list);
            y += 208;

            _btnOk = new Button { Text = "Применить", Left = 178, Top = y, Width = 90, Height = 28 };
            _btnOk.Click += (s, e) => OnOk();
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text         = "Отмена", Left = 276, Top = y, Width = 90, Height = 28,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_btnCancel);

            Height       = y + 68;
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void OnOk()
        {
            if (_list.SelectedItem == null)
            {
                MessageDlg.Show("Выберите конфигурацию из списка.");
                return;
            }
            SelectedState = _list.SelectedItem.ToString();
            DialogResult  = DialogResult.OK;
            Close();
        }
    }
}
