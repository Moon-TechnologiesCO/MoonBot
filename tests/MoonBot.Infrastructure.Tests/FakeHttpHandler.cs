using System.Net;
using System.Text;

namespace MoonBot.Infrastructure.Tests;

// "Cable" falso de HttpClient: devuelve la respuesta que le digamos y guarda la petición.
internal sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
    }
}

// Simula que no hay nadie escuchando en esa dirección.
internal sealed class RefusingHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new HttpRequestException("No connection could be made");
}
