namespace Repl;

public sealed partial class CoreReplApp
{
	private DocumentationEngine? _documentationEngine;
	private DocumentationEngine DocumentationEng => _documentationEngine ??= new(this);

	/// <inheritdoc />
	public ReplDocumentationModel CreateDocumentationModel(string? targetPath = null) =>
		DocumentationEng.CreateDocumentationModel(targetPath);

	/// <summary>
	/// Builds a structured documentation model using the specified provider for service-backed parameters.
	/// </summary>
	/// <param name="serviceProvider">Provider used to determine whether direct handler parameters can be omitted.</param>
	/// <param name="targetPath">Optional target path to scope the model.</param>
	/// <returns>A structured documentation model.</returns>
	public ReplDocumentationModel CreateDocumentationModel(
		IServiceProvider serviceProvider,
		string? targetPath = null) =>
		DocumentationEng.CreateDocumentationModel(serviceProvider, targetPath);

	/// <summary>
	/// Internal documentation model creation that supports not-found result for help rendering.
	/// </summary>
	internal object CreateDocumentationModelInternal(string? targetPath) =>
		DocumentationEng.CreateDocumentationModelInternal(targetPath);

	internal ReplDocApp BuildDocumentationApp() =>
		DocumentationEng.BuildDocumentationApp();
}
