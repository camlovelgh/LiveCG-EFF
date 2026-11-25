using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class BroadcastUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string n)=>(T)window.FindName(n);object? Call(string n,params object?[] values){var m=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var path=Path.Combine(folder,"타임라인 예제.png");var region=new EffectRegion{Bounds=new(.1,.1,.5,.5),Clips=[new(){Options=new(){Kind=EffectKind.Ripple}}]};window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=[region]}]});Pump();
        var strength=C<ComboBox>("StrengthBox");check(strength.Items.Cast<string>().SequenceEqual(new[]{"최약","약","중","강","최강"}),"five strength labels appear in intuitive strength order");strength.SelectedIndex=0;Pump();check(region.Clips[0].Options.Strength==StrengthLevel.VeryWeak,"lowest strength changes selected clip to very weak");strength.SelectedIndex=4;Pump();check(region.Clips[0].Options.Strength==StrengthLevel.VeryStrong,"highest strength changes selected clip to very strong");
        Call("SetEffectColor",new SKColor(12,230,250,180));check(region.Clips[0].Options.Color=="#B40CE6FA"&&C<Border>("EffectColorSwatch").Background is SolidColorBrush brush&&brush.Color.G==230,"effect color picker result updates clip and visible swatch including alpha");Call("UndoEdit",window,new RoutedEventArgs());Call("RedoEdit",window,new RoutedEventArgs());
        var current=(ImageJob)typeof(MainWindow).GetField("_current",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;check(current.Regions[0].Clips[0].Options.Color=="#B40CE6FA","effect color participates in undo and redo");Call("ResetEffectColor",window,new RoutedEventArgs());check(current.Regions[0].Clips[0].Options.Color is null,"default-color button restores per-effect colors");
        var tabs=C<TabControl>("EffectsTabs");
        var cards=tabs.Items.Cast<TabItem>().ToArray();
        tabs.UpdateLayout();
        check(cards.Select(card=>card.Header).SequenceEqual(new[]{"기본 효과","NEW 효과","클립 속성","내 효과","즐겨찾기","그룹·마스크"}),
            "effect tabs use the requested labels and navigation order");
        var positions=cards.Select(card=>card.TranslatePoint(new Point(),tabs)).ToArray();
        check(positions[0].Y==positions[1].Y&&positions[1].Y==positions[2].Y&&positions[3].Y==positions[4].Y&&positions[4].Y==positions[5].Y
            &&positions[0].Y<positions[3].Y&&positions[0].X<positions[1].X&&positions[1].X<positions[2].X
            &&positions[3].X<positions[4].X&&positions[4].X<positions[5].X&&cards.All(card=>card.ActualHeight>=44&&card.ActualWidth>=70),
            "effect tabs are arranged in a 3/3 layout with usable hit targets");
        var timeline=C<TimelineControl>("TimelineEditor");
        var clip=current.Regions[0].Clips[0];
        var frame=1d/30;
        check(window.HandleEditorKey(Key.Right,ModifierKeys.None,timeline)&&Math.Abs(clip.Start-frame)<1e-8,
            "right arrow still nudges the selected clip by one frame");
        check(window.HandleEditorKey(Key.Right,ModifierKeys.Shift,timeline)&&Math.Abs(clip.Start-11*frame)<1e-8
            &&window.HandleEditorKey(Key.Left,ModifierKeys.Shift,timeline)&&Math.Abs(clip.Start-frame)<1e-8,
            "Shift plus either timeline arrow moves the selected clip exactly ten frames");
        window.HandleEditorKey(Key.Left,ModifierKeys.Shift,timeline);
        check(Math.Abs(clip.Start-frame)<1e-8,"ten-frame movement leaves the selection unchanged at the zero boundary");

        var basic=C<System.Windows.Controls.Primitives.UniformGrid>("EffectsGrid").Children.OfType<EffectThumbnail>()
            .First(thumb=>thumb.Kind==EffectKind.Glow);
        var favoriteMenuItem=(MenuItem)basic.ContextMenu.Items[0];
        check(Equals(favoriteMenuItem.Header,"즐겨찾기에 추가"),"basic effect exposes a right-click favorite action");
        favoriteMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var favoriteList=C<ItemsControl>("FavoriteList");
        var added=favoriteList.Items.Cast<object>().Single();
        Call("ApplyFavorite",new Button{Tag=added},new RoutedEventArgs());
        check(clip.Options.Kind==EffectKind.Glow,"clicking a favorite applies the effect to the selected clip");

        var choices=C<ItemsControl>("FavoriteChoices");
        var presetChoice=choices.Items.Cast<object>().First(choice=>
        {
            var entry=choice.GetType().GetProperty("Entry")!.GetValue(choice)!;
            return entry.GetType().GetProperty("Bundled")!.GetValue(entry) is true;
        });
        var presetEntry=presetChoice.GetType().GetProperty("Entry")!.GetValue(presetChoice);
        Call("ChangeFavoriteChoice",new CheckBox{Tag=presetEntry,IsChecked=true},new RoutedEventArgs());
        check(favoriteList.Items.Count==2,"the favorites picker adds a preset by clicking its checkbox");
        var reopened=new MainWindow();
        check(((ItemsControl)reopened.FindName("FavoriteList")).Items.Count==2,"favorites are restored on restart");
        reopened.Close();
        Call("RemoveFavorite",new Button{Tag=presetEntry},new RoutedEventArgs());
        Call("RemoveFavorite",new Button{Tag=added},new RoutedEventArgs());
        check(favoriteList.Items.Count==0&&C<TextBlock>("FavoriteEmpty").Visibility==Visibility.Visible,
            "removing both favorites updates the list and the empty state");
        var studio=new EffectStudioWindow(EffectAsset.NewMotion()){Owner=window,Left=-15000,Top=-15000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
        try{studio.Show();Pump();check(studio.Timeline.Motion.Tracks.Count==0,"new motion studio begins with no prefilled sample animation");studio.SetKey(MotionProperty.CropTop,0,.2,KeyEasing.Linear);studio.SetKey(MotionProperty.CropRight,60,.7,KeyEasing.EaseInOut);check(studio.Timeline.Motion.Value(MotionProperty.CropTop,15)==.2&&studio.Timeline.Motion.Value(MotionProperty.CropRight,60)==.7,"studio can author top and right crop keyframes");studio.Timeline.Refresh();check(studio.Timeline.Height>=MotionTimelineControl.RulerHeight+12*MotionTimelineControl.RowHeight,"motion timeline exposes all twelve channels including four crop rows");StudioUiChecks.Capture(studio,Path.Combine(folder,"broadcast-crop-studio.png"),1370,940);}finally{studio.DiscardAndClose();}
        tabs.SelectedIndex=1;StudioUiChecks.Capture(window,Path.Combine(folder,"broadcast-effect-tabs.png"),1580,1040);
    }
}
