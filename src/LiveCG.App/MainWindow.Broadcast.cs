using System.Windows;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private void DuplicateBroadcastSource(object sender, RoutedEventArgs e)
    {
        if (_busy || _current is null || !CommitClip(true) || !CommitRegion(true) || !CommitMask(true)) return;
        var duplicate = _current.Snapshot();
        duplicate.BroadcastId = Guid.NewGuid();
        duplicate.OutputPath = ""; duplicate.OutputAliases.Clear();
        duplicate.Status = "새 방송 자료 복제";
        _jobs.Add(duplicate);
        FileList.SelectedItem = duplicate;
        MarkEdited(true);
        RefreshFiles();
        Notify("새 방송 자료 ID로 복제했습니다. 원본 이미지 교체 후 작업을 저장하세요.");
    }
}
