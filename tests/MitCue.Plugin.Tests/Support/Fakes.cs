using System.Reflection;

namespace MitCue.PluginTests.Support;

/// <summary>
/// A stand-in for a Dalamud service interface: records every call and returns the return type's default (null for
/// reference types), so code under test sees "no data" from IDataManager, IJobGauges and the like.
/// </summary>
public class ServiceFake : DispatchProxy
{
    /// <summary>Every call made through the proxy: method name and arguments.</summary>
    public List<(string Name, object?[] Args)> Calls { get; } = [];

    /// <summary>Creates a stand-in for <typeparamref name="T"/> and its call log.</summary>
    public static (T Service, ServiceFake Log) Create<T>()
        where T : class
    {
        var proxy = DispatchProxy.Create<T, ServiceFake>();
        return (proxy, (ServiceFake)(object)proxy);
    }

    /// <summary>A stand-in for <typeparamref name="T"/> when the calls do not matter.</summary>
    public static T Of<T>()
        where T : class => Create<T>().Service;

    /// <summary>The first argument of each call to <paramref name="method"/>, as text (chat messages).</summary>
    public IEnumerable<string> FirstArgs(string method) =>
        this.Calls.Where(c => c.Name == method).Select(c => c.Args.Length > 0 ? c.Args[0]?.ToString() ?? string.Empty : string.Empty);

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var method = targetMethod!;
        this.Calls.Add((method.Name, args ?? []));
        var type = method.ReturnType;
        return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    }
}
