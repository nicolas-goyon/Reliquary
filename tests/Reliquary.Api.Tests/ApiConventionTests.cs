using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Reliquary.Api.Conventions;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Api.Tests;

public sealed class ApiConventionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UnknownApiPathReturnsNotFoundProblemWithCodeAndTraceId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/unknown", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ErrorCodes.NotFound, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        Assert.False(problem.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task WrongMethodReturnsMethodNotAllowedProblem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/v1/status", UriKind.Relative), null, TestContext.Current.CancellationToken);
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(ErrorCodes.MethodNotAllowed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReadOnlyOwnerExceptionBecomesServiceUnavailableWithRetryAfter()
    {
        var (status, body, context) = await HandleAsync(new OwnerReadOnlyException(Guid.NewGuid()));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal(ErrorCodes.OwnerReadOnly, body.GetProperty("code").GetString());
        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task UnexpectedExceptionBecomesInternalErrorWithoutDetails()
    {
        var (status, body, _) = await HandleAsync(new InvalidOperationException("secret detail"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(ErrorCodes.InternalError, body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret detail", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationProblemListsEachFieldError()
    {
        var result = ApiProblems.Validation(new FieldError("email", FieldErrorCodes.Required, "The email is necessary."));
        var context = NewContext();

        await result.ExecuteAsync(context);
        var body = await ReadBodyAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal("email", error.GetProperty("field").GetString());
        Assert.Equal(FieldErrorCodes.Required, error.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static async Task<(int Status, JsonElement Body, HttpContext Context)> HandleAsync(Exception exception)
    {
        var context = NewContext();
        var handler = new ApiExceptionHandler(context.RequestServices.GetRequiredService<IProblemDetailsService>(), NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken));
        return (context.Response.StatusCode, await ReadBodyAsync(context), context);
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddApiConventions();
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }
}
