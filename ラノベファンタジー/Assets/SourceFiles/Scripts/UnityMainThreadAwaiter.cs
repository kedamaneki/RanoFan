using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
using Object = UnityEngine.Object;
#endif

/// <summary>
/// HttpClient 等のスレッドプール継続後に Unity API を安全に呼ぶためのヘルパー。
/// await 後の継続はスレッドプールに戻るため、Unity 処理はキュー内で完結させます。
/// </summary>
public static class UnityMainThreadAwaiter
{
#if UNITY_5_3_OR_NEWER
    private static int mainThreadId;
    private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
    private static DispatcherBehaviour dispatcher;

    private sealed class DispatcherBehaviour : MonoBehaviour
    {
        private void Awake()
        {
            RegisterMainThreadId();
        }

        private void Update()
        {
            DrainQueue();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CaptureMainThread()
    {
        RegisterMainThreadId();
        EnsureDispatcher();
    }

    private static void RegisterMainThreadId()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public static bool IsMainThread =>
        mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId;

    private static void EnsureDispatcher()
    {
        if (dispatcher != null)
        {
            return;
        }

        var host = new GameObject(nameof(UnityMainThreadAwaiter));
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        dispatcher = host.AddComponent<DispatcherBehaviour>();
    }

    private static void DrainQueue()
    {
        while (Queue.TryDequeue(out Action action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
#endif

    public static Task SwitchToMainThreadAsync(CancellationToken cancellationToken = default)
    {
        return RunOnMainThreadAsync(static () => { }, cancellationToken);
    }

    public static Task RunOnMainThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();

#if UNITY_5_3_OR_NEWER
        if (IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        EnsureDispatcher();
        return completion.Task;
#else
        action();
        return Task.CompletedTask;
#endif
    }

    public static Task<T> RunOnMainThreadAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        cancellationToken.ThrowIfCancellationRequested();

#if UNITY_5_3_OR_NEWER
        if (IsMainThread)
        {
            return Task.FromResult(func());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(func());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        EnsureDispatcher();
        return completion.Task;
#else
        return Task.FromResult(func());
#endif
    }

    public static void RunOnMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }

#if UNITY_5_3_OR_NEWER
        if (IsMainThread)
        {
            action();
            return;
        }

        Queue.Enqueue(action);
        EnsureDispatcher();
#else
        action();
#endif
    }
}
