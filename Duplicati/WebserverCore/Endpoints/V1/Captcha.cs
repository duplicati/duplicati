using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Captcha : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/captcha/{token}", ([FromServices] ICaptchaProvider captchaProvider, [FromServices] IHttpContextAccessor httpContextAccessor, [FromRoute] string token, CancellationToken ct) =>
        {
            var jpeg = captchaProvider.GetCaptchaImage(token);
            var response = httpContextAccessor.HttpContext!.Response;
            response.ContentLength = jpeg.Length;
            response.ContentType = "image/jpeg";
            response.Body.WriteAsync(jpeg, ct);
        });

        group.MapPost("/captcha", ([FromServices] ICaptchaProvider captchaProvider, [FromBody] Dto.SolveCaptchaInputDto input) =>
        {
            var (token, answer) = captchaProvider.CreateCaptcha(input.target);
            return new Dto.GenerateCaptchaOutput(
                token,
                answer,
                captchaProvider.VisualCaptchaDisabled
            );
        })
        .RequireAuthorization();


        group.MapPost("/captcha/{token}", ([FromServices] ICaptchaProvider captchaProvider, [FromRoute] string token, [FromBody] Dto.SolveCaptchaInputDto input) =>
        {
            if (captchaProvider.SolvedCaptcha(token, input.target, input.answer ?? ""))
                return new { success = true };
            return new { success = false };
        })
        .RequireAuthorization();

    }
}
