using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FlexTool;

public partial class MainWindow
{
    // ── Pawn Editor ──────────────────────────────────────────────

    private UIElement BuildPawnEditor()
    {
        _selectedPawn ??= _colonyPawns.FirstOrDefault();

        var root = new StackPanel();

        // Title + Apply button row
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Pawn Editor",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var applyBtn = new Button
        {
            Content = "⬆ Apply to Game",
            Style = (Style)FindResource("LaunchButtonStyle"),
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Send current pawn edits to the running game via IPC"
        };
        DockPanel.SetDock(applyBtn, Dock.Right);
        applyBtn.Click += (_, _) => ApplyPawnEditsToGame();
        titleRow.Children.Add(applyBtn);
        root.Children.Add(titleRow);

        // Colonist dropdown
        root.Children.Add(BuildPawnSelector());

        if (_selectedPawn is null)
        {
            var blank = new PawnData();
            _colonyPawns.Add(blank);
            _selectedPawn = blank;
        }

        // Tab strip
        root.Children.Add(BuildPawnEditorTabStrip());

        // Tab content
        root.Children.Add(BuildPawnEditorTabContent());

        return root;
    }

    private UIElement BuildPawnEditorTabStrip()
    {
        var tabs = new[] { "Bio", "Skills", "Health", "Gear" };

        var bar = new Border
        {
            BorderBrush  = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };

        foreach (var tab in tabs)
        {
            bool isActive = _pawnEditorTab == tab;
            var capturedTab = tab;

            var btn = new Border
            {
                Padding = new Thickness(22, 9, 22, 10),
                Cursor  = Cursors.Hand,
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x22, 0x3A, 0x7B, 0xD5))
                    : Brushes.Transparent,
                BorderBrush = isActive
                    ? (Brush)FindResource("BluePrimaryBrush")
                    : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, isActive ? 2.5 : 0)
            };

            btn.Child = new TextBlock
            {
                Text       = tab,
                FontSize   = 13,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isActive
                    ? (Brush)FindResource("BlueLightBrush")
                    : (Brush)FindResource("TextSecondaryBrush")
            };

            btn.MouseLeftButtonDown += (_, _) =>
            {
                _pawnEditorTab = capturedTab;
                UpdateContentArea();
            };

            sp.Children.Add(btn);
        }

        bar.Child = sp;
        return bar;
    }

    private UIElement BuildPawnEditorTabContent()
    {
        var wrapper = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

        UIElement content = _pawnEditorTab switch
        {
            "Skills" => BuildSkillsTabContent(),
            "Health" => BuildHealthTabContent(),
            "Gear"   => BuildGearTabContent(),
            _        => BuildBioTabContent()   // "Bio" + default
        };

        wrapper.Children.Add(content);
        return wrapper;
    }

    private UIElement BuildBioTabContent()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildBiographySection());
        panel.Children.Add(BuildAppearanceSection());
        return panel;
    }

    private UIElement BuildSkillsTabContent()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildSkillsSection());
        panel.Children.Add(BuildTraitsSection());
        return panel;
    }

    private UIElement BuildHealthTabContent()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildHealthSection());
        return panel;
    }

    private UIElement BuildGearTabContent()
    {
        var card = new Border
        {
            Background      = (Brush)FindResource("PanelMidBrush"),
            BorderBrush     = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(20)
        };

        var stack = new StackPanel();
        var gear = _selectedPawn?.Gear ?? [];

        if (gear.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text       = "No gear found for this pawn — gear is read from the most recent save file.",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize   = 13,
                FontStyle  = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var group in gear.GroupBy(g => g.Kind))
            {
                stack.Children.Add(new TextBlock
                {
                    Text       = group.Key,
                    FontSize   = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("BlueLightBrush"),
                    Margin     = new Thickness(0, stack.Children.Count == 0 ? 0 : 12, 0, 6)
                });

                foreach (var item in group)
                {
                    var line = item.Name;
                    if (!string.IsNullOrEmpty(item.Quality)) line += $" ({item.Quality})";
                    if (item.HitPoints >= 0) line += $" — {item.HitPoints} HP";

                    stack.Children.Add(new TextBlock
                    {
                        Text       = "• " + line,
                        FontSize   = 12,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        Margin     = new Thickness(6, 0, 0, 4),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            stack.Children.Add(new TextBlock
            {
                Text       = "Gear is read-only — edit items in-game or via the Cheats menu.",
                FontSize   = 10,
                FontStyle  = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin     = new Thickness(0, 12, 0, 0)
            });
        }

        card.Child = stack;
        return card;
    }

    private UIElement BuildPawnSelector()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 320 });

        var lbl = new TextBlock
        {
            Text = "Colonist",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lbl, 0);

        var cb = new ComboBox { Style = FindResource("DarkComboBoxStyle") as Style };
        int selectedIndex = 0;
        for (int i = 0; i < _colonyPawns.Count; i++)
        {
            var pawn = _colonyPawns[i];
            var display = string.IsNullOrEmpty(pawn.Nickname)
                ? $"{pawn.FirstName} {pawn.LastName}"
                : $"{pawn.FirstName} \"{pawn.Nickname}\" {pawn.LastName}";
            cb.Items.Add(display);
            if (pawn == _selectedPawn)
                selectedIndex = i;
        }
        // Defer selection to avoid SelectionChanged firing during initialization
        if (selectedIndex >= 0)
            Dispatcher.BeginInvoke(() => cb.SelectedIndex = selectedIndex, DispatcherPriority.Render);
        cb.SelectionChanged += (_, _) =>
        {
            try
            {
                if (cb?.SelectedIndex >= 0 && cb.SelectedIndex < _colonyPawns.Count)
                {
                    _selectedPawn = _colonyPawns[cb.SelectedIndex];
                    UpdateContentArea();
                }
            }
            catch { /* Ignore selection change errors */ }
        };
        Grid.SetColumn(cb, 1);

        row.Children.Add(lbl);
        row.Children.Add(cb);
        return row;
    }

    private Border BuildEditorCard(string title, UIElement content)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    private UIElement BuildFieldRow(string label, UIElement editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = (FindResource("TextSecondaryBrush") as Brush) ?? new SolidColorBrush(Colors.LightGray),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(editor);
        return grid;
    }

    private TextBox MakeTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            Style = FindResource("DarkTextBoxStyle") as Style,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private ComboBox MakeComboBox(string[] items, int selectedIndex = 0)
    {
        var cb = new ComboBox
        {
            Style = FindResource("DarkComboBoxStyle") as Style,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var item in items)
            cb.Items.Add(item);

        // Defer selection to avoid SelectionChanged firing during initialization
        if (items.Length > selectedIndex && selectedIndex >= 0)
            Dispatcher.BeginInvoke(() => cb.SelectedIndex = selectedIndex, DispatcherPriority.Render);

        return cb;
    }

    // ── Biography ──

    private Border BuildBiographySection()
    {
        var p = _selectedPawn!;
        var panel = new StackPanel();

        var firstNameBox = MakeTextBox(p.FirstName);
        firstNameBox.TextChanged += (_, _) => p.FirstName = firstNameBox.Text;
        panel.Children.Add(BuildFieldRow("First Name", firstNameBox));

        var nicknameBox = MakeTextBox(p.Nickname);
        nicknameBox.TextChanged += (_, _) => p.Nickname = nicknameBox.Text;
        panel.Children.Add(BuildFieldRow("Nickname", nicknameBox));

        var lastNameBox = MakeTextBox(p.LastName);
        lastNameBox.TextChanged += (_, _) => p.LastName = lastNameBox.Text;
        panel.Children.Add(BuildFieldRow("Last Name", lastNameBox));

        string[] genders = ["Female", "Male"];
        var genderCb = MakeComboBox(genders, Array.IndexOf(genders, p.Gender));
        genderCb.SelectionChanged += (_, _) =>
        {
            try { if (genderCb?.SelectedItem is string g) p.Gender = g; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Gender", genderCb));

        var bioAgeBox = MakeTextBox(p.BioAge.ToString());
        bioAgeBox.TextChanged += (_, _) => { if (int.TryParse(bioAgeBox.Text, out int v)) p.BioAge = v; };
        panel.Children.Add(BuildFieldRow("Bio Age", bioAgeBox));

        var chronoAgeBox = MakeTextBox(p.ChronoAge.ToString());
        chronoAgeBox.TextChanged += (_, _) => { if (int.TryParse(chronoAgeBox.Text, out int v)) p.ChronoAge = v; };
        panel.Children.Add(BuildFieldRow("Chrono Age", chronoAgeBox));

        string[] childhoods = ["Vatgrown Soldier", "Urbworld Urchin", "Medieval Lord", "Cave Child", "Midworld Cadet"];
        var childhoodCb = MakeComboBox(childhoods, Math.Max(0, Array.IndexOf(childhoods, p.Childhood)));
        childhoodCb.SelectionChanged += (_, _) =>
        {
            try { if (childhoodCb?.SelectedItem is string c) p.Childhood = c; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Childhood", childhoodCb));

        string[] adulthoods = ["Marine", "Drifter", "Sheriff", "Researcher", "Doctor"];
        var adulthoodCb = MakeComboBox(adulthoods, Math.Max(0, Array.IndexOf(adulthoods, p.Adulthood)));
        adulthoodCb.SelectionChanged += (_, _) =>
        {
            try { if (adulthoodCb?.SelectedItem is string a) p.Adulthood = a; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Adulthood", adulthoodCb));

        return BuildEditorCard("Biography", panel);
    }

    // ── Appearance ──

    private Border BuildAppearanceSection()
    {
        var p = _selectedPawn!;
        var panel = new StackPanel();

        string[] bodyTypes = ["Thin", "Average", "Hulk", "Fat", "Male", "Female"];
        var bodyTypeCb = MakeComboBox(bodyTypes, Math.Max(0, Array.IndexOf(bodyTypes, p.BodyType)));
        bodyTypeCb.SelectionChanged += (_, _) =>
        {
            try { if (bodyTypeCb?.SelectedItem is string v) p.BodyType = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Body Type", bodyTypeCb));

        string[] headTypes = ["Average_Normal", "Average_Wide", "Average_Pointy", "Narrow_Normal", "Narrow_Squint"];
        var headTypeCb = MakeComboBox(headTypes, Math.Max(0, Array.IndexOf(headTypes, p.HeadType)));
        headTypeCb.SelectionChanged += (_, _) =>
        {
            try { if (headTypeCb?.SelectedItem is string v) p.HeadType = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Head Type", headTypeCb));

        string[] hairs = ["Shaved", "Bob", "Mohawk", "Topdog", "Afro", "Pigtails", "Flowy", "Long"];
        var hairCb = MakeComboBox(hairs, Math.Max(0, Array.IndexOf(hairs, p.Hair)));
        hairCb.SelectionChanged += (_, _) =>
        {
            try { if (hairCb?.SelectedItem is string v) p.Hair = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Hair", hairCb));

        string[] hairColors = ["Brown", "Blonde", "Black", "Red", "White", "Gray"];
        var hairColorCb = MakeComboBox(hairColors, Math.Max(0, Array.IndexOf(hairColors, p.HairColor)));
        hairColorCb.SelectionChanged += (_, _) =>
        {
            try { if (hairColorCb?.SelectedItem is string v) p.HairColor = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Hair Color", hairColorCb));

        string[] skinColors = ["Light", "Medium", "Dark", "Pale"];
        var skinColorCb = MakeComboBox(skinColors, Math.Max(0, Array.IndexOf(skinColors, p.SkinColor)));
        skinColorCb.SelectionChanged += (_, _) =>
        {
            try { if (skinColorCb?.SelectedItem is string v) p.SkinColor = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Skin Color", skinColorCb));

        string[] beards = ["None", "Full Beard", "Goatee", "Handlebar", "Stubble"];
        var beardCb = MakeComboBox(beards, Math.Max(0, Array.IndexOf(beards, p.Beard)));
        beardCb.SelectionChanged += (_, _) =>
        {
            try { if (beardCb?.SelectedItem is string v) p.Beard = v; }
            catch { /* Ignore selection change errors */ }
        };
        panel.Children.Add(BuildFieldRow("Beard", beardCb));

        return BuildEditorCard("Appearance", panel);
    }

    // ── Skills ──

    private static readonly string[] AllSkillNames =
    [
        "Shooting", "Melee", "Construction", "Mining", "Cooking",
        "Plants", "Animals", "Crafting", "Artistic", "Medicine",
        "Social", "Intellectual"
    ];

    private Border BuildSkillsSection()
    {
        var pawn = _selectedPawn!;
        var panel = new StackPanel();

        string[] passionNames = ["None", "Minor", "Major"];

        // Build a lookup so parsed-save skills are matched by name; unknown saves still show all rows
        var skillMap = pawn.Skills.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var skillName in AllSkillNames)
        {
            // Use existing SkillData if present, otherwise create a default entry and track it
            if (!skillMap.TryGetValue(skillName, out var skill))
            {
                skill = new SkillData { Name = skillName, Level = 0, Passion = 0 };
                pawn.Skills.Add(skill);
                skillMap[skillName] = skill;
            }

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var lbl = new TextBlock
            {
                Text = skill.Name,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueLbl = new TextBlock
            {
                Text = skill.Level.ToString(),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var capturedSkill = skill;
            var slider = new Slider
            {
                Style = (Style)FindResource("DarkSliderStyle"),
                Value = skill.Level
            };
            var capturedValueLbl = valueLbl;
            slider.ValueChanged += (_, args) =>
            {
                capturedValueLbl.Text = ((int)args.NewValue).ToString();
                capturedSkill.Level = (int)args.NewValue;
            };

            var passionCb = new ComboBox
            {
                Style = FindResource("DarkComboBoxStyle") as Style,
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2)
            };
            foreach (var p in passionNames) passionCb.Items.Add(p);

            // Store reference for deferred selection and handlers
            var capturedPb = passionCb;
            var deferred = Math.Clamp(skill.Passion, 0, 2);
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (capturedPb?.Items.Count > deferred)
                        capturedPb.SelectedIndex = deferred;
                }
                catch { /* Ignore initialization errors */ }
            }, DispatcherPriority.Render);

            passionCb.SelectionChanged += (_, _) =>
            {
                try
                {
                    if (passionCb?.SelectedIndex >= 0)
                        capturedSkill.Passion = passionCb.SelectedIndex;
                }
                catch { /* Ignore selection change errors */ }
            };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(valueLbl, 2);
            Grid.SetColumn(passionCb, 3);

            row.Children.Add(lbl);
            row.Children.Add(slider);
            row.Children.Add(valueLbl);
            row.Children.Add(passionCb);

            panel.Children.Add(row);
        }

        return BuildEditorCard("Skills", panel);
    }

    // ── Traits ──

    private Border BuildTraitsSection()
    {
        var pawn = _selectedPawn!;
        var panel = new StackPanel();

        string[] allTraits = ["Tough", "Psychopath", "Iron-Willed", "Industrious", "Bloodlust",
            "Quick Sleeper", "Nimble", "Too Smart", "Sanguine", "Ascetic",
            "Brawler", "Jogger", "Kind", "Neurotic", "Transhumanist"];

        if (pawn.Traits.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No traits",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        foreach (var trait in pawn.Traits)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = trait,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var capturedTrait = trait;
            var removeBtn = new Button
            {
                Content = "✕",
                Style = (Style)FindResource("CardDangerButtonStyle")
            };
            removeBtn.Click += (_, _) =>
            {
                pawn.Traits.Remove(capturedTrait);
                UpdateContentArea();
            };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(removeBtn, 1);
            row.Children.Add(lbl);
            row.Children.Add(removeBtn);
            panel.Children.Add(row);
        }

        // Add trait row
        var addRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var traitPicker = MakeComboBox(allTraits, 0);
        var addBtn = new Button
        {
            Content = "+ Add",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        addBtn.Click += (_, _) =>
        {
            if (traitPicker.SelectedItem is string selected && !pawn.Traits.Contains(selected))
            {
                pawn.Traits.Add(selected);
                UpdateContentArea();
            }
        };

        Grid.SetColumn(traitPicker, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(traitPicker);
        addRow.Children.Add(addBtn);
        panel.Children.Add(addRow);

        return BuildEditorCard("Traits", panel);
    }

    // ── Health ──

    private Border BuildHealthSection()
    {
        var pawn = _selectedPawn!;
        var panel = new StackPanel();

        foreach (var hc in pawn.HealthConditions)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = hc.Condition,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var partLbl = new TextBlock
            {
                Text = hc.BodyPart,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var capturedHc = hc;
            var removeBtn = new Button
            {
                Content = "✕",
                Style = (Style)FindResource("CardDangerButtonStyle")
            };
            removeBtn.Click += (_, _) =>
            {
                pawn.HealthConditions.Remove(capturedHc);
                UpdateContentArea();
            };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(partLbl, 1);
            Grid.SetColumn(removeBtn, 2);
            row.Children.Add(lbl);
            row.Children.Add(partLbl);
            row.Children.Add(removeBtn);
            panel.Children.Add(row);
        }

        if (pawn.HealthConditions.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No health conditions",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        string[] allConditions = ["Frail", "Bad Back", "Hearing Loss", "Asthma", "Dementia",
            "Artery Blockage", "Cataract", "Gunshot", "Plague", "Malaria"];

        // Add row
        var addRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var condPicker = MakeComboBox(allConditions);
        var addBtn = new Button
        {
            Content = "+ Add",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        addBtn.Click += (_, _) =>
        {
            if (condPicker.SelectedItem is string selected)
            {
                pawn.HealthConditions.Add(new HealthCondition { Condition = selected, BodyPart = "Whole Body" });
                UpdateContentArea();
            }
        };

        Grid.SetColumn(condPicker, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(condPicker);
        addRow.Children.Add(addBtn);
        panel.Children.Add(addRow);

        return BuildEditorCard("Health", panel);
    }

    private Viewbox CreateSidebarIcon(string name, bool isActive)
    {
        var fill = isActive ? Brushes.White : (Brush)FindResource("TextSecondaryBrush");
        var path = new Path { Fill = fill };

        // Use a simple circle/dot icon as default
        path.Data = Geometry.Parse(
            "M12 12c2.7 0 4.8-2.2 4.8-4.8S14.7 2.4 12 2.4 7.2 4.5 7.2 7.2 9.3 12 12 12z" +
            "m0 2.4c-3.2 0-9.6 1.6-9.6 4.8v2.4h19.2v-2.4c0-3.2-6.4-4.8-9.6-4.8z");

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);

        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 10, 0),
            Child = canvas
        };
    }

    // ── Pawn Editor Apply to Game ───────────────────────────────

    private void ApplyPawnEditsToGame()
    {
        if (_selectedPawn is null)
        {
            ShowToast("No Pawn Selected", "Select a pawn to apply edits.", ToastService.ToastType.Warning);
            return;
        }

        if (!IsGameRunning())
        {
            ShowToast("Game Not Running", "Start RimWorld before applying pawn edits.", ToastService.ToastType.Warning);
            return;
        }

        var targetName = !string.IsNullOrEmpty(_selectedPawn.Nickname)
            ? _selectedPawn.Nickname
            : $"{_selectedPawn.FirstName} {_selectedPawn.LastName}".Trim();

        if (string.IsNullOrWhiteSpace(targetName))
        {
            ShowToast("Invalid Pawn", "Pawn has no name to identify.", ToastService.ToastType.Warning);
            return;
        }

        var skills = _selectedPawn.Skills
            .Where(s => s.Level >= 0)
            .Select(s => (s.Name, s.Level))
            .ToList();

        RimWorldSaveReader.SendPawnEdit(
            targetName,
            _selectedPawn.FirstName,
            _selectedPawn.Nickname,
            _selectedPawn.LastName,
            skills);

        ShowToast("Pawn Edit Sent", $"Sending edits for {targetName} to game…", ToastService.ToastType.Info);

        // Poll for result
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        int attempts = 0;
        timer.Tick += (_, _) =>
        {
            attempts++;
            var result = RimWorldSaveReader.ReadPawnEditResult();
            if (result != null)
            {
                timer.Stop();
                bool isError = result.Value.Result.StartsWith("ERROR");
                ShowToast(
                    isError ? "Edit Failed" : "Pawn Updated",
                    result.Value.Result,
                    isError ? ToastService.ToastType.Error : ToastService.ToastType.Success);
            }
            else if (attempts > 10)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }
}
