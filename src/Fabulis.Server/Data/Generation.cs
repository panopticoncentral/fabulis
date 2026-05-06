using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Fabulis.Server.Api;

namespace Fabulis.Server.Data;

public enum GenerationStatus { Running, Completed, Aborted, Failed }

/// <summary>
/// One in-flight or recently-completed model generation for a draft. Owned by
/// <see cref="GenerationManager"/> and outlives any individual HTTP request: a
/// client can disconnect (phone locks, app backgrounded) and the generation
/// keeps streaming from the LLM, accumulating content. A new HTTP request can
/// re-attach via <see cref="SubscribeAsync"/>, which yields a `snapshot`
/// envelope (full content so far) followed by live deltas, or a terminal
/// envelope if the generation has already finished.
/// </summary>
public sealed class Generation(int draftId)
{
    public int DraftId { get; } = draftId;
    public CancellationTokenSource Cts { get; } = new();

    private readonly object _gate = new();
    private readonly StringBuilder _content = new();
    private readonly List<Channel<StreamEnvelope>> _subs = new();
    private GenerationStatus _status = GenerationStatus.Running;
    private int? _savedMessageId;
    private string? _error;

    public string Snapshot { get { lock (_gate) return _content.ToString(); } }
    public int ContentLength { get { lock (_gate) return _content.Length; } }
    public GenerationStatus Status { get { lock (_gate) return _status; } }

    public void Append(string text, bool isReasoning)
    {
        var env = new StreamEnvelope("chunk", text, isReasoning, null);
        lock (_gate)
        {
            if (!isReasoning) _content.Append(text);
            foreach (var c in _subs) c.Writer.TryWrite(env);
        }
    }

    public void Complete(int? savedMessageId, GenerationStatus terminal)
    {
        List<Channel<StreamEnvelope>> snapshot;
        StreamEnvelope env;
        lock (_gate)
        {
            if (_status != GenerationStatus.Running) return;
            _status = terminal;
            _savedMessageId = savedMessageId;
            env = new StreamEnvelope("done", null, null, savedMessageId);
            snapshot = new List<Channel<StreamEnvelope>>(_subs);
            _subs.Clear();
        }
        foreach (var c in snapshot)
        {
            c.Writer.TryWrite(env);
            c.Writer.TryComplete();
        }
    }

    public void Fail(string error)
    {
        List<Channel<StreamEnvelope>> snapshot;
        StreamEnvelope env;
        lock (_gate)
        {
            if (_status != GenerationStatus.Running) return;
            _status = GenerationStatus.Failed;
            _error = error;
            env = new StreamEnvelope("error", error, null, null);
            snapshot = new List<Channel<StreamEnvelope>>(_subs);
            _subs.Clear();
        }
        foreach (var c in snapshot)
        {
            c.Writer.TryWrite(env);
            c.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerable<StreamEnvelope> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamEnvelope snapshotEnv;
        StreamEnvelope? terminalEnv = null;
        Channel<StreamEnvelope>? channel = null;

        // Take snapshot and register subscriber under the same lock as
        // Append/Complete/Fail so we can't miss chunks: anything appended
        // before this lock is in the snapshot, anything appended after goes
        // through the channel.
        lock (_gate)
        {
            snapshotEnv = new StreamEnvelope("snapshot", _content.ToString(), false, null);
            switch (_status)
            {
                case GenerationStatus.Completed:
                case GenerationStatus.Aborted:
                    terminalEnv = new StreamEnvelope("done", null, null, _savedMessageId);
                    break;
                case GenerationStatus.Failed:
                    terminalEnv = new StreamEnvelope("error", _error, null, null);
                    break;
                default:
                    channel = Channel.CreateUnbounded<StreamEnvelope>();
                    _subs.Add(channel);
                    break;
            }
        }

        yield return snapshotEnv;

        if (terminalEnv is not null)
        {
            yield return terminalEnv;
            yield break;
        }

        try
        {
            await foreach (var env in channel!.Reader.ReadAllAsync(ct))
            {
                yield return env;
            }
        }
        finally
        {
            lock (_gate) { _subs.Remove(channel!); }
        }
    }
}
