using System.IO;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private sealed record FavoriteEntry(string Name, EffectKind? Kind=null, EffectAsset? Asset=null, bool Bundled=false);
    private sealed record FavoriteChoice(FavoriteEntry Entry, bool Added);
    private EffectFavorites _favorites=new([],[]);
    private static string FavoritesPath=>Path.Combine(DocumentStore.DataDirectory,"favorite-effects.json");

    private void LoadFavoriteSettings()
    {
        if(!File.Exists(FavoritesPath))return;
        try
        {
            _favorites=DocumentStore.Load<EffectFavorites>(FavoritesPath);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify("즐겨찾기를 읽지 못했습니다 · "+ex.Message);
        }
    }

    private bool IsFavorite(FavoriteEntry entry)
    {
        if(entry.Kind is { } kind)return _favorites.Effects.Contains(kind);
        if(entry.Asset is not { } asset)return false;
        return entry.Bundled?_favorites.Presets.Contains(asset.Id):
            _assets.Any(item=>item.Id==asset.Id&&item.Favorite);
    }

    private void RefreshFavorites()
    {
        var catalog=_thumbnails.Select(item=>new FavoriteEntry(ProductInfo.EffectName(item.Kind),Kind:item.Kind))
            .Concat(_bundledEffects.Select(asset=>new FavoriteEntry(asset.Name,Asset:asset,Bundled:true)))
            .Concat(_assets.Select(asset=>new FavoriteEntry(asset.Name,Asset:asset)))
            .ToList();
        var saved=catalog.Where(IsFavorite).ToList();
        FavoriteChoices.ItemsSource=catalog.Select(entry=>new FavoriteChoice(entry,IsFavorite(entry))).ToList();
        FavoriteList.ItemsSource=saved;
        FavoriteEmpty.Visibility=saved.Count==0?Visibility.Visible:Visibility.Collapsed;
    }

    private void SetFavorite(FavoriteEntry entry,bool add)
    {
        if(_busy){RefreshFavorites();return;}
        try
        {
            if(entry.Asset is { } personal&&!entry.Bundled)
            {
                var previous=personal.Favorite;
                personal.Favorite=add;
                try{AssetStore.Save(personal);}
                catch{personal.Favorite=previous;throw;}
                RefreshAssets();
            }
            else
            {
                var settings=new EffectFavorites(new(_favorites.Effects),new(_favorites.Presets));
                if(entry.Kind is { } kind)
                {
                    if(add)settings.Effects.Add(kind);else settings.Effects.Remove(kind);
                }
                else if(entry.Asset is { } preset)
                {
                    if(add)settings.Presets.Add(preset.Id);else settings.Presets.Remove(preset.Id);
                }
                DocumentStore.Save(FavoritesPath,settings);
                _favorites=settings;
                RefreshFavorites();
            }
            Notify(add?$"즐겨찾기에 추가 · {entry.Name}":$"즐겨찾기에서 제거 · {entry.Name}");
        }
        catch(Exception ex){RefreshFavorites();Error(ex);}
    }

    private ContextMenu CreateFavoriteMenu(FavoriteEntry entry)
    {
        var menu=new ContextMenu();
        var item=AddFavoriteMenuItem(menu,entry);
        menu.Opened+=(_,_)=>UpdateFavoriteMenuItem(item,entry);
        return menu;
    }

    private MenuItem AddFavoriteMenuItem(ContextMenu menu,FavoriteEntry entry)
    {
        var item=new MenuItem();
        UpdateFavoriteMenuItem(item,entry);
        item.Click+=(_,_)=>SetFavorite(entry,!IsFavorite(entry));
        menu.Items.Add(item);
        return item;
    }

    private void UpdateFavoriteMenuItem(MenuItem item,FavoriteEntry entry)
    {
        item.Header=IsFavorite(entry)?"즐겨찾기에서 제거":"즐겨찾기에 추가";
        item.IsEnabled=!_busy;
    }

    private void OpenAssetFavoriteMenu(object sender,ContextMenuEventArgs e)
    {
        if(sender is not FrameworkElement { DataContext:EffectAsset asset, ContextMenu:{ } menu } element)return;
        menu.Items.Clear();
        AddFavoriteMenuItem(menu,new(asset.Name,Asset:asset,Bundled:Equals(element.Tag,"Bundled")));
    }

    private void ChangeFavoriteChoice(object sender,RoutedEventArgs e)
    {if(sender is CheckBox { Tag:FavoriteEntry entry } box)SetFavorite(entry,box.IsChecked==true);}

    private void RemoveFavorite(object sender,RoutedEventArgs e)
    {if(sender is Button { Tag:FavoriteEntry entry })SetFavorite(entry,false);}

    private void ApplyFavorite(object sender,RoutedEventArgs e)
    {
        if(sender is not Button { Tag:FavoriteEntry entry }||_busy)return;
        if(_region is null){Notify("먼저 효과를 적용할 영역을 선택하세요.");return;}
        if(entry.Kind is { } kind)SelectEffect(kind);
        else if(entry.Asset is { } asset)
        {
            if(entry.Bundled)PlaceBundledPreset(asset,false);
            else InsertEffectAsset(asset);
        }
    }
}
