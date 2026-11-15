using System.Windows;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private void EditBlurFocus(object sender, RoutedEventArgs e)
    {
        if (_busy || _current is not { } job) return;
        if (job.Regions.Count == 0 && job.BlurFocus is null)
        {
            Notify("먼저 강조할 영역을 하나 이상 직접 지정하세요.");
            return;
        }
        StopPlayback();
        try
        {
            var editor = new BlurFocusWindow(job, _region?.Id) { Owner = this };
            if (editor.ShowDialog() != true) return;
            job.BlurFocus = editor.Result;
            MarkEdited();
            Notify(job.BlurFocus is null ? "순차 블러 강조 해제" : "순차 블러 강조 적용 · OBS 단축키로 전환");
        }
        catch (Exception error) { Error(error); }
    }
}
