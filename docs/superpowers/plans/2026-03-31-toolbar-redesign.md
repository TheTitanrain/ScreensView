# Toolbar Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the standard WPF `ToolBar` in `MainWindow.xaml` with a flat-light custom panel using Segoe MDL2 Assets icons, styled buttons with hover/pressed/disabled states, and a proper ToggleButton for polling control.

**Architecture:** The `<ToolBar>` is replaced with a `<Border>` + `<Grid>` container. All button styles (`ToolbarButton`, `ToolbarButtonGhost`, `ToolbarToggleButton`) and separator style (`ToolbarSeparator`) are defined in `Window.Resources` using full `ControlTemplate`s to enable custom hover, pressed, and disabled states. No new files are created — all changes are in `MainWindow.xaml`.

**Tech Stack:** WPF / XAML, .NET 8, `Segoe MDL2 Assets` font (built into Windows 10/11)

**Spec:** `docs/superpowers/specs/2026-03-31-toolbar-redesign.md`

---

## File Map

| File | Change |
|---|---|
| `ScreensView.Viewer/MainWindow.xaml` | Replace `<ToolBar>` with `<Border>` container; add 4 styles to `Window.Resources`; add `MinWidth="700"` to `<Window>` |

No new files. No code-behind changes.

---

### Task 1: Add Window MinWidth

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml:1-8`

- [ ] **Step 1: Add `MinWidth="700"` to the `<Window>` element**

Open `ScreensView.Viewer/MainWindow.xaml`. The `<Window>` tag currently looks like:

```xml
<Window x:Class="ScreensView.Viewer.MainWindow"
        ...
        Title="ScreensView" Height="700" Width="1100"
        WindowState="Maximized"
```

Add `MinWidth="700"` after `Width="1100"`:

```xml
<Window x:Class="ScreensView.Viewer.MainWindow"
        ...
        Title="ScreensView" Height="700" Width="1100"
        MinWidth="700"
        WindowState="Maximized"
```

- [ ] **Step 2: Build and verify no errors**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml
git commit -m "feat: add MinWidth to MainWindow to prevent toolbar clipping"
```

---

### Task 2: Add Resource Styles to Window.Resources

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml:10-17` (Window.Resources section)

The current `<Window.Resources>` block contains only the `StatusIndicator` ellipse style. We add four new styles here. The order matters for readability but not for WPF compilation.

- [ ] **Step 1: Add `ToolbarSeparator` style**

Inside `<Window.Resources>`, after the existing `StatusIndicator` style, add:

```xml
<Style x:Key="ToolbarSeparator" TargetType="Rectangle">
    <Setter Property="Width" Value="1"/>
    <Setter Property="Height" Value="26"/>
    <Setter Property="Fill" Value="#D0D0D0"/>
    <Setter Property="Margin" Value="6,0"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
</Style>
```

- [ ] **Step 2: Add `ToolbarButton` style**

```xml
<Style x:Key="ToolbarButton" TargetType="Button">
    <Setter Property="Foreground" Value="#1A1A1A"/>
    <Setter Property="Margin" Value="0,0,4,0"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border x:Name="border"
                        Background="White" BorderBrush="#D0D0D0" BorderThickness="1"
                        CornerRadius="5" Padding="5,6,12,6">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="border" Property="Background" Value="#E8E8E8"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="border" Property="Background" Value="#D0D0D0"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="border" Property="Background" Value="#F0F0F0"/>
                        <Setter TargetName="border" Property="BorderBrush" Value="#D8D8D8"/>
                        <Setter Property="Foreground" Value="#AAAAAA"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 3: Add `ToolbarButtonGhost` style**

```xml
<Style x:Key="ToolbarButtonGhost" TargetType="Button">
    <Setter Property="Foreground" Value="#555555"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border x:Name="border" Background="Transparent"
                        CornerRadius="5" Padding="5,4,10,4">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="border" Property="Background" Value="#E8E8E8"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="border" Property="Background" Value="#D0D0D0"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Foreground" Value="#AAAAAA"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 4: Add `ToolbarToggleButton` style**

```xml
<!--
    Foreground is not set at style level — the button always renders on a colored
    background (green or red), so text is hardcoded White inside the ControlTemplate
    for guaranteed contrast. This is intentional.
-->
<Style x:Key="ToolbarToggleButton" TargetType="ToggleButton">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ToggleButton">
                <Border x:Name="border" Background="#107C10" CornerRadius="5" Padding="5,6,12,6">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="icon"
                                   FontFamily="Segoe MDL2 Assets" FontSize="14"
                                   Text="&#xE768;" Foreground="White" VerticalAlignment="Center"/>
                        <TextBlock x:Name="label"
                                   Text=" Запустить" Foreground="White"
                                   FontWeight="SemiBold" VerticalAlignment="Center"/>
                    </StackPanel>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="border" Property="Background" Value="#C50500"/>
                        <Setter TargetName="icon" Property="Text" Value="&#xE71A;"/>
                        <Setter TargetName="label" Property="Text" Value=" Остановить"/>
                    </Trigger>
                    <!-- Opacity used for hover/press so it works in both checked and unchecked
                         states without conflicting with the Background set by IsChecked trigger -->
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="border" Property="Opacity" Value="0.88"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="border" Property="Opacity" Value="0.75"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="border" Property="Opacity" Value="0.45"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 5: Build and verify no errors**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors. (Styles are defined but not yet used — no visual change yet.)

- [ ] **Step 6: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml
git commit -m "feat: add toolbar styles to Window.Resources (not yet applied)"
```

---

### Task 3: Replace ToolBar with Custom Border Container

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml:21-53` (the entire `<ToolBar>` block)

This is the main replacement. The current `<ToolBar>` block (lines 21–53) is replaced in full. All existing bindings and click handlers must be preserved exactly.

- [ ] **Step 1: Replace the `<ToolBar>` block with the new `<Border>` container**

Delete lines 21–53 (the entire `<ToolBar>...</ToolBar>`) and replace with:

```xml
<!-- Toolbar -->
<Border DockPanel.Dock="Top" Height="44"
        Background="#F3F3F3"
        BorderBrush="#D0D0D0" BorderThickness="0,0,0,1">
    <Grid Margin="8,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <!-- Left: main controls -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">

            <Button Style="{StaticResource ToolbarButton}" Click="ManageComputers_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE7EF;" FontSize="14" VerticalAlignment="Center"/>
                    <TextBlock Text=" Компьютеры" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>

            <Rectangle Style="{StaticResource ToolbarSeparator}"/>

            <TextBlock Text="Интервал (сек):" VerticalAlignment="Center"
                       FontSize="12" Foreground="#555555" Margin="4,0"/>
            <Slider x:Name="IntervalSlider" Minimum="1" Maximum="60" Width="110"
                    VerticalAlignment="Center" Value="{Binding RefreshInterval}"
                    Margin="4,0" TickFrequency="1" IsSnapToTickEnabled="True"/>
            <TextBlock Text="{Binding RefreshInterval, StringFormat='{}{0} с'}"
                       VerticalAlignment="Center" Width="35"
                       FontSize="13" FontWeight="SemiBold" Foreground="#1A1A1A"/>

            <Rectangle Style="{StaticResource ToolbarSeparator}"/>

            <ToggleButton Command="{Binding TogglePollingCommand}"
                          IsChecked="{Binding IsPolling, Mode=OneWay}"
                          MinWidth="110"
                          Style="{StaticResource ToolbarToggleButton}"/>

            <Rectangle Style="{StaticResource ToolbarSeparator}"/>

            <Button Style="{StaticResource ToolbarButton}" Click="UpdateAllAgents_Click">
                <StackPanel Orientation="Horizontal">
                    <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE895;" FontSize="14" VerticalAlignment="Center"/>
                    <TextBlock Text=" Обновить агентов" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>

            <Rectangle Style="{StaticResource ToolbarSeparator}"/>

            <CheckBox Content="Автозапуск"
                      IsChecked="{Binding IsAutostartEnabled}"
                      VerticalAlignment="Center"
                      Margin="8,0,4,0"/>

        </StackPanel>

        <!-- Right: About button -->
        <Button Grid.Column="1" Style="{StaticResource ToolbarButtonGhost}"
                Click="About_Click" VerticalAlignment="Center">
            <StackPanel Orientation="Horizontal">
                <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE946;" FontSize="13" VerticalAlignment="Center"/>
                <TextBlock Text=" О программе" FontSize="12" VerticalAlignment="Center"/>
            </StackPanel>
        </Button>

    </Grid>
</Border>
```

- [ ] **Step 2: Build and verify no errors**

```bash
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the Viewer and visually verify**

```bash
dotnet run --project ScreensView.Viewer
```

Check:
- Toolbar has light grey background with bottom border line
- "Компьютеры" button has icon + label, white background, rounded border
- Interval slider and value label are present
- "Запустить" button is green; clicking it turns it red and shows "Остановить"
- "Обновить агентов" button has icon + label
- "Автозапуск" checkbox is present
- "О программе" button is right-aligned, ghost style
- Hover on buttons shows grey background
- Window cannot be resized narrower than 700px

- [ ] **Step 4: Commit**

```bash
git add ScreensView.Viewer/MainWindow.xaml
git commit -m "feat: replace ToolBar with flat-light custom toolbar panel"
```
