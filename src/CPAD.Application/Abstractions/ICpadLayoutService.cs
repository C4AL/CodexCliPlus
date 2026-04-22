using CPAD.Domain;

namespace CPAD.Application.Abstractions;

public interface ICpadLayoutService
{
    CpadLayout Resolve();
}
