using SJMirror.App.Models;

namespace SJMirror.App.Services;

public interface IMirrorSettingsStore
{
    Task<MirrorSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MirrorSettings settings, CancellationToken cancellationToken = default);
}
