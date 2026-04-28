using CommunityToolkit.Mvvm.ComponentModel;

namespace CodexCliPlus.ViewModels.Pages;

public sealed class LogsPageState : ObservableObject
{
    private string _requestId = string.Empty;
    private string _requestLogContent = string.Empty;
    private string _requestLogError = string.Empty;
    private bool _requestLogFound;
    private bool _isIncrementalResult;
    private RequestLogLookupState? _lastLookupState;

    public string RequestId
    {
        get => _requestId;
        set => SetProperty(ref _requestId, value);
    }

    public string RequestLogContent
    {
        get => _requestLogContent;
        private set
        {
            if (SetProperty(ref _requestLogContent, value))
            {
                OnPropertyChanged(nameof(HasRequestLogContent));
                OnPropertyChanged(nameof(RequestLogEmptyTitle));
                OnPropertyChanged(nameof(RequestLogEmptyDescription));
            }
        }
    }

    public string RequestLogError
    {
        get => _requestLogError;
        private set
        {
            if (SetProperty(ref _requestLogError, value))
            {
                OnPropertyChanged(nameof(HasRequestLogError));
                OnPropertyChanged(nameof(RequestLogEmptyTitle));
                OnPropertyChanged(nameof(RequestLogEmptyDescription));
            }
        }
    }

    public bool RequestLogFound
    {
        get => _requestLogFound;
        private set
        {
            if (SetProperty(ref _requestLogFound, value))
            {
                OnPropertyChanged(nameof(HasRequestLogContent));
                OnPropertyChanged(nameof(RequestLogEmptyTitle));
                OnPropertyChanged(nameof(RequestLogEmptyDescription));
            }
        }
    }

    public bool IsIncrementalResult
    {
        get => _isIncrementalResult;
        private set
        {
            if (SetProperty(ref _isIncrementalResult, value))
            {
                OnPropertyChanged(nameof(FeedModeText));
            }
        }
    }

    public bool HasRequestLogContent => RequestLogFound && !string.IsNullOrWhiteSpace(RequestLogContent);

    public bool HasRequestLogError => !string.IsNullOrWhiteSpace(RequestLogError);

    public string FeedModeText => IsIncrementalResult ? "增量刷新" : "全量刷新";

    public string RequestLogEmptyTitle
    {
        get
        {
            if (HasRequestLogError)
            {
                return _lastLookupState == RequestLogLookupState.NotFound
                    ? "未找到请求日志"
                    : "请求日志加载失败";
            }

            return "输入请求编号后查看详情";
        }
    }

    public string RequestLogEmptyDescription
    {
        get
        {
            if (HasRequestLogError)
            {
                return RequestLogError;
            }

            return "请求日志会在当前页面内打开，不再弹出额外窗口。";
        }
    }

    public void MarkFeedMode(bool incremental)
    {
        IsIncrementalResult = incremental;
    }

    public void ResetRequestLog()
    {
        _lastLookupState = null;
        OnPropertyChanged(nameof(RequestLogEmptyTitle));
        RequestLogFound = false;
        RequestLogContent = string.Empty;
        RequestLogError = string.Empty;
    }

    public void ApplyRequestLogResult(string requestId, RequestLogLookupResult result)
    {
        RequestId = requestId;
        _lastLookupState = result.State;
        OnPropertyChanged(nameof(RequestLogEmptyTitle));

        switch (result.State)
        {
            case RequestLogLookupState.Found:
                RequestLogFound = true;
                RequestLogContent = result.Content;
                RequestLogError = string.Empty;
                break;
            case RequestLogLookupState.NotFound:
                RequestLogFound = false;
                RequestLogContent = string.Empty;
                RequestLogError = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"未找到请求编号为 {requestId} 的日志。"
                    : result.ErrorMessage;
                break;
            default:
                RequestLogFound = false;
                RequestLogContent = string.Empty;
                RequestLogError = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "请求日志加载失败。"
                    : result.ErrorMessage;
                break;
        }
    }
}

public enum RequestLogLookupState
{
    Found,
    NotFound,
    Failed
}

public sealed record RequestLogLookupResult(
    RequestLogLookupState State,
    string Content = "",
    string ErrorMessage = "")
{
    public static RequestLogLookupResult Found(string content)
    {
        return new(RequestLogLookupState.Found, content);
    }

    public static RequestLogLookupResult NotFound(string message)
    {
        return new(RequestLogLookupState.NotFound, ErrorMessage: message);
    }

    public static RequestLogLookupResult Failed(string message)
    {
        return new(RequestLogLookupState.Failed, ErrorMessage: message);
    }
}
