using System.Text;
using System.Xml;
using TableClothLite.Shared.Models;

namespace TableClothLite.Services;

/// <summary>
/// 무설치(Express) 식탁보 .wsb 생성기. TableCloth 호스트 앱 설치 없이, 생성된 .wsb 하나를 더블클릭하면
/// Windows Sandbox 안에서 무설치 런처가 최신 포터블 식탁보(Spork)를 받아 실행한다.
///
/// 계약: docs/PARAMETERIZED_WSB_SPEC.md §0.5 "간소화된 기본형"(TableCloth 리포). 동작 흐름:
///   보이는 PowerShell 창 → DNS 선보정(probe-then-fallback) → TLS 1.2 → 고정 URL의
///   tablecloth-prepare.ps1 을 iex 로 무파일 실행 → 준비 스크립트가 SporkBootstrap_{arch}.exe 를 받아 실행.
/// 사이트 딥링크는 환경 변수 TABLECLOTH_SITE_IDS 로 전달한다(iex 는 인자 전달 불가).
///
/// 주의: .wsb 의 LogonCommand 는 ASCII 전용이어야 한다. Windows PowerShell 5.1 은 비-ASCII 를 레거시
/// ANSI 코드페이지로 해석해 파싱이 깨질 수 있어, 표시 문자열까지 모두 영문으로 둔다(현지화는 런처 GUI 담당).
/// </summary>
public sealed class SandboxComposerService
{
    // 고정 URL(버전 무관, GitHub latest/download 별칭)의 준비 스크립트. 이 스크립트가 arch 판별과
    // 런처(SporkBootstrap_{arch}.exe) 다운로드/실행을 담당한다 → 로직 변경이 .wsb 재배포 없이 반영됨.
    private const string PrepareScriptUrl =
        "https://github.com/yourtablecloth/TableCloth/releases/latest/download/tablecloth-prepare.ps1";

    private readonly ConfigService _configService;

    public SandboxComposerService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task<XmlDocument> CreateSandboxDocumentAsync(
        SandboxService sandboxService,
        ServiceInfo? serviceInfo,
        CancellationToken cancellationToken = default)
    {
        var model = await _configService.LoadAsync(cancellationToken).ConfigureAwait(false);

        var doc = new XmlDocument();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
        doc.AppendChild(doc.CreateComment(
            " No-install TableCloth launcher. See docs/PARAMETERIZED_WSB_SPEC.md (section 0.5). "));

        var configuration = doc.CreateElement("Configuration");

        // 무설치 런처는 준비 스크립트/런처 다운로드를 위해 네트워크가 반드시 필요하므로 항상 Enable.
        AppendElement(doc, configuration, "Networking", "Enable");
        AppendElement(doc, configuration, "vGPU", "Disable");

        // 사용자 기기 옵션(설정에서 조정). 폴더 마운트는 두지 않는다 → 호스트 파일 접근 0.
        AppendElement(doc, configuration, "AudioInput", model.EnableAudioInput ? "Enable" : "Disable");
        AppendElement(doc, configuration, "VideoInput", model.EnableVideoInput ? "Enable" : "Disable");
        AppendElement(doc, configuration, "PrinterRedirection", model.EnablePrinterRedirection ? "Enable" : "Disable");
        AppendElement(doc, configuration, "ClipboardRedirection", model.EnableClipboardRedirection ? "Enable" : "Disable");

        var logonCommand = doc.CreateElement("LogonCommand");
        var command = doc.CreateElement("Command");
        command.InnerText = BuildLogonCommand(serviceInfo?.ServiceId);
        logonCommand.AppendChild(command);
        configuration.AppendChild(logonCommand);

        doc.AppendChild(configuration);
        return doc;
    }

    // PARAMETERIZED_WSB_SPEC §0.5 간소화된 기본형의 LogonCommand.
    // WSB 는 LogonCommand 콘솔을 숨긴 채 실행하므로, 보이는 PowerShell 창 하나를 새로 띄우고 그 안에서
    // DNS 선보정 → TLS 1.2 → 준비 스크립트 iex 실행을 수행한다. 사이트 딥링크는 env(TABLECLOTH_SITE_IDS).
    private static string BuildLogonCommand(string? siteId)
    {
        var sanitized = SanitizeSiteIds(siteId);
        var siteIdsStmt = string.IsNullOrEmpty(sanitized)
            ? string.Empty
            : $"$env:TABLECLOTH_SITE_IDS = '{sanitized}'; ";

        // 보이는 창 안에서 실행될 스크립트(일반 작은따옴표 표기). ASCII 전용.
        var inner =
            "$Host.UI.RawUI.WindowTitle = 'TableCloth Setup'; " +
            "Write-Host ' Getting TableCloth ready...' -ForegroundColor Cyan; " +
            "if (-not (Resolve-DnsName -Name github.com -QuickTimeout -ErrorAction SilentlyContinue)) " +
            "{ Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses 8.8.8.8,1.1.1.1 }; " +
            "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; " +
            siteIdsStmt +
            "try { iex ((New-Object Net.WebClient).DownloadString('" + PrepareScriptUrl + "')) } " +
            "catch { Write-Host (' Failed: ' + $_.Exception.Message) -ForegroundColor Red; $null = Read-Host ' Press Enter to close' }";

        // Start-Process -ArgumentList 의 단일 요소로 넣기 위해 내부 작은따옴표를 '' 로 이스케이프.
        var escapedInner = inner.Replace("'", "''");

        var outer =
            "Start-Process powershell.exe -WindowStyle Normal -ArgumentList " +
            "'-NoProfile','-ExecutionPolicy','Bypass','-Command','" + escapedInner + "'";

        return "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"" + outer + "\"";
    }

    // 사이트 Id는 카탈로그 화이트리스트에서 오지만, PowerShell 문자열 안전을 위해 방어적으로
    // 영숫자/공백/일부 구분자만 허용한다(작은따옴표 등 제거 → 인젝션 불가).
    private static string SanitizeSiteIds(string? siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return string.Empty;

        var sb = new StringBuilder(siteId.Length);
        foreach (var ch in siteId.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '_' || ch == '-' || ch == '.')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void AppendElement(XmlDocument doc, XmlElement parent, string name, string value)
    {
        var element = doc.CreateElement(name);
        element.InnerText = value;
        parent.AppendChild(element);
    }
}
