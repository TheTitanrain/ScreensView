# Toolbar Redesign — Design Spec
Date: 2026-03-31

## Goal

Replace the default WPF `ToolBar` in `MainWindow.xaml` with a polished flat-light panel using Segoe MDL2 Assets icons and custom button styles.

## Visual Direction

**Style:** Light / Flat (Windows 11-inspired)
**Icons:** Segoe MDL2 Assets (`FontFamily="Segoe MDL2 Assets"`)
**Implementation approach:** Replace `<ToolBar>` with `<Border>` + `<Grid>`, define styles in `Window.Resources`.

## Layout & Colors

| Element | Value |
|---|---|
| Toolbar background | `#F3F3F3` |
| Bottom border | `1px solid #D0D0D0` |
| Toolbar height | `44px` (was 40px — intentional increase) |
| Button background (normal) | `White` |
| Button border | `1px solid #D0D0D0` |
| Button corner radius | `5px` |
| Button hover background | `#E8E8E8` |
| Button pressed background | `#D0D0D0` |
| Button foreground | `#1A1A1A` |
| Button padding | `5,6,12,6` |

## Container Structure

Replace `<ToolBar>` with:

```xml
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
            <!-- buttons here -->
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

## Buttons (Left StackPanel)

Each button sets `Content` to an inline `StackPanel` — not a `ContentTemplate`. Example:

```xml
<Button Style="{StaticResource ToolbarButton}" Click="ManageComputers_Click">
    <StackPanel Orientation="Horizontal">
        <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE7EF;" FontSize="14" VerticalAlignment="Center"/>
        <TextBlock Text=" Компьютеры" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

| Button | Icon codepoint | Handler |
|---|---|---|
| Компьютеры | `&#xE7EF;` | `ManageComputers_Click` |
| Обновить агентов | `&#xE895;` | `UpdateAllAgents_Click` |

## Separator

Use `<Rectangle>` with explicit `Style="{StaticResource ToolbarSeparator}"` at every usage site:

```xml
<Rectangle Style="{StaticResource ToolbarSeparator}"/>
```

Style:

```xml
<Style x:Key="ToolbarSeparator" TargetType="Rectangle">
    <Setter Property="Width" Value="1"/>
    <Setter Property="Height" Value="26"/>
    <Setter Property="Fill" Value="#D0D0D0"/>
    <Setter Property="Margin" Value="6,0"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
</Style>
```

## ToggleButton (Запустить / Остановить)

The icon glyph changes between states. This requires a full `ControlTemplate` with two named `TextBlock`s and `Trigger`s on `IsChecked`. Preserve existing bindings as attributes on the element.

```xml
<ToggleButton Command="{Binding TogglePollingCommand}"
              IsChecked="{Binding IsPolling, Mode=OneWay}"
              MinWidth="110"
              Style="{StaticResource ToolbarToggleButton}"/>
```

Full style definition:

```xml
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
                    <!-- IsMouseOver uses Opacity so it works in both checked and unchecked states
                         without conflicting with the Background set by the IsChecked trigger -->
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="border" Property="Opacity" Value="0.88"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="border" Property="Opacity" Value="0.75"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## Button Style (with ControlTemplate for hover)

WPF's default `Button` template uses `SystemColors` brushes internally, which override a plain `Background` setter. A `ControlTemplate` is required for custom hover/pressed states:

```xml
<Style x:Key="ToolbarButton" TargetType="Button">
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
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## Ghost Button Style (О программе)

Transparent background, no visible border, `Foreground=#555555`. Also requires a `ControlTemplate`:

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
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## Interval Controls

Preserve existing `x:Name="IntervalSlider"` and bindings:

```xml
<TextBlock Text="Интервал (сек):" VerticalAlignment="Center"
           FontSize="12" Foreground="#555555" Margin="4,0"/>
<Slider x:Name="IntervalSlider" Minimum="1" Maximum="60" Width="110"
        VerticalAlignment="Center" Value="{Binding RefreshInterval}"
        Margin="4,0" TickFrequency="1" IsSnapToTickEnabled="True"/>
<TextBlock Text="{Binding RefreshInterval, StringFormat='{}{0} с'}"
           VerticalAlignment="Center" Width="35"
           FontSize="13" FontWeight="SemiBold" Foreground="#1A1A1A"/>
```

The `Slider` keeps its default WPF template. Custom thumb styling is **out of scope** for this task.

## CheckBox (Автозапуск)

Preserve existing binding. No visual changes — `CheckBox` styling is out of scope for this iteration:

```xml
<CheckBox Content="Автозапуск"
          IsChecked="{Binding IsAutostartEnabled}"
          VerticalAlignment="Center"
          Margin="8,0,4,0"/>
```

## Styles Location

All styles defined in `MainWindow.xaml` under `<Window.Resources>`:

- `Style x:Key="ToolbarButton" TargetType="Button"` — standard button with ControlTemplate
- `Style x:Key="ToolbarButtonGhost" TargetType="Button"` — ghost style for "О программе"
- `Style x:Key="ToolbarSeparator" TargetType="Rectangle"` — separator line
- `Style x:Key="ToolbarToggleButton" TargetType="ToggleButton"` — with ControlTemplate and IsChecked triggers

## Out of Scope

- Slider thumb custom styling
- Styling of card tiles, other windows, or context menus
- Tooltip changes
- Dark mode support
- Accessibility / high-contrast themes
