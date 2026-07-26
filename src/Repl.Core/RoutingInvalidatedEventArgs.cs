namespace Repl;

internal sealed class RoutingInvalidatedEventArgs(bool isVisibilityRetraction) : EventArgs
{
	internal bool IsVisibilityRetraction { get; } = isVisibilityRetraction;
}
