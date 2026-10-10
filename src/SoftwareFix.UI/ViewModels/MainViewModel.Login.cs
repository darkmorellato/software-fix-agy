using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareFix.Core;
using SoftwareFix.Core.Services;

namespace SoftwareFix.UI.ViewModels;

/// <summary>
/// Tela inicial de login (Lenovo ID / Google) do Software Fix Linux:
///   1. gera o link oficial de login (OAuth2 Lenovo ID, com PKCE/state);
///   2. abre no navegador padrão;
///   3. o usuário entra com Google/Lenovo ID e a página final "lenovoIdSuccess"
///      traz o token (lenovoid.wust= / Authorization=) na URL;
///   4. o usuário cola a URL de volta e o app valida e salva a sessão.
/// Com a sessão ativa, o catálogo OFICIAL completo de modelos é carregado.
/// </summary>
public partial class MainViewModel
{
    private readonly LenovoApiClient _loginApi = new();
    private string? _loginUrlCache;
    private DispatcherTimer? _loginPollTimer;
    private DateTime _sessionSeenAt = DateTime.MinValue;

    [ObservableProperty] private bool _isLoginOpen;
    [ObservableProperty] private string _loginStatusText = "";
    [ObservableProperty] private string _loginUrlText = "";
    [ObservableProperty] private string _loginPastedText = "";
    [ObservableProperty] private bool _loginBusy;
    [ObservableProperty] private bool _loginSuccess;
    [ObservableProperty] private string _loginUserName = "";

    public string LoginWelcomeText => LoginSuccess
        ? $"Conta conectada: {LoginUserName}"
        : "Use sua conta Lenovo ID ou Google para acessar o catálogo oficial de firmware.";

    // ---- Status da conta (header: abaixo de "Software Fix v7.6.2.10") ----
    private static readonly IBrush _statusGreen = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush _statusRed = new SolidColorBrush(Color.Parse("#EF4444"));

    [ObservableProperty] private bool _isAccountConnected;

    public string AccountStatusText => IsAccountConnected ? "Conectado" : "Não conectado";
    public IBrush AccountStatusBrush => IsAccountConnected ? _statusGreen : _statusRed;

    partial void OnIsAccountConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(AccountStatusText));
        OnPropertyChanged(nameof(AccountStatusBrush));
    }

    partial void OnLoginSuccessChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginWelcomeText));
        if (value) IsAccountConnected = true;
    }

    partial void OnLoginUserNameChanged(string value) => OnPropertyChanged(nameof(LoginWelcomeText));

    // ---- Tela inicial (réplica do oficial): aceites + fluxo ----
    [ObservableProperty] private bool _acceptPrivacy = true;
    [ObservableProperty] private bool _acceptGoogleNotice = true;
    [ObservableProperty] private bool _loginFlowStarted;

    public bool CanLogin => AcceptPrivacy && AcceptGoogleNotice;
    public string AppVersionText => "Versão: 7.6.2.10";

    partial void OnAcceptPrivacyChanged(bool value) => OnPropertyChanged(nameof(CanLogin));
    partial void OnAcceptGoogleNoticeChanged(bool value) => OnPropertyChanged(nameof(CanLogin));

    [RelayCommand]
    private void StartLoginFlow()
    {
        LoginFlowStarted = true;
        AddLog("[Login] Fluxo de login iniciado (tela inicial).");
        if (CanLogin)
        {
            _ = OpenLoginInBrowserAsync();
        }
    }

    [RelayCommand]
    private void OpenPrivacyPolicy()
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", "https://www.lenovo.com/br/pt/privacy/") { UseShellExecute = false });
            AddLog("[Login] Política de Privacidade da Lenovo aberta no navegador.");
        }
        catch { }
    }

    [RelayCommand]
    private void OpenLoginScreen()
    {
        IsLoginOpen = true;
        if (LoginSuccess)
        {
            LoginStatusText = $"Conta conectada: {LoginUserName}";
        }
        else if (_loginApi.TryLoadSession())
        {
            // sessão salva: valida ao vivo e já mostra como conectado
            LoginStatusText = "Sessão salva encontrada — validando com o servidor...";
            _ = ValidateSavedSessionAsync();
        }
        else
        {
            LoginStatusText = "Passo 1 — clique em \"Abrir página de login no navegador\".";
        }
        _ = EnsureLoginUrlAsync();
        StartLoginPoll();
        AddLog("[Login] Tela de login aberta.");
    }

    /// <summary>
    /// Vigia o arquivo de sessão enquanto a tela de login estiver aberta: quando o
    /// handler do scheme "softwareFix://" (navegador) salvar o login, a GUI se
    /// conecta sozinha — sem o usuário precisar colar nada.
    /// </summary>
    private void StartLoginPoll()
    {
        try
        {
            if (File.Exists(Configurations.SessionFile))
                _sessionSeenAt = File.GetLastWriteTime(Configurations.SessionFile);
        }
        catch { }
        if (_loginPollTimer is null)
        {
            _loginPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _loginPollTimer.Tick += (_, _) => PollLoginSession();
        }
        _loginPollTimer.Start();
    }

    private void PollLoginSession()
    {
        if (!IsLoginOpen || LoginSuccess)
        {
            _loginPollTimer?.Stop();
            return;
        }
        try
        {
            if (File.Exists(Configurations.SessionFile))
            {
                var mtime = File.GetLastWriteTime(Configurations.SessionFile);
                if (mtime > _sessionSeenAt)
                {
                    _sessionSeenAt = mtime;
                    LoginStatusText = "Login capturado do navegador! Validando...";
                    AddLog("[Login] Sessão salva detectada no disco — validando.");
                    _ = ValidateSavedSessionAsync();
                }
            }
        }
        catch { }
    }

    private async Task ValidateSavedSessionAsync()
    {
        try
        {
            LoginBusy = true;
            await _loginApi.FetchPublicKeyAsync();
            var u = await _loginApi.GetUserInfoAsync();
            if (u.Success)
            {
                LoginSuccess = true;
                LoginUserName = u.Content?["name"]?.ToString()
                    ?? u.Content?["email"]?.ToString()
                    ?? "(conta conectada)";
                LoginStatusText = "Sessão válida — você já está conectado.";
                await LoadOfficialCatalogAsync();
            }
            else
            {
                LoginStatusText = "Sessão expirada — faça login novamente abaixo.";
            }
        }
        catch
        {
            LoginStatusText = "Não foi possível validar a sessão salva agora.";
        }
        finally
        {
            LoginBusy = false;
        }
    }

    [RelayCommand]
    private void CloseLoginScreen()
    {
        IsLoginOpen = false;
        _loginPollTimer?.Stop();
        AddLog("[Login] Tela fechada.");
    }

    [RelayCommand]
    private async Task OpenLoginInBrowserAsync()
    {
        try
        {
            string? url = await EnsureLoginUrlAsync();
            if (string.IsNullOrEmpty(url))
            {
                LoginStatusText = "Não foi possível gerar o link de login (verifique a conexão com a internet).";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
                LoginStatusText = "Navegador aberto. Faça login com Google/Lenovo ID e depois volte aqui (passo 3).";
            }
            catch
            {
                LoginStatusText = "Não consegui abrir o navegador automaticamente — copie o link abaixo e abra manualmente.";
            }
            AddLog("[Login] Link de login aberto/gerado.");
        }
        catch (Exception ex)
        {
            LoginStatusText = "Erro ao preparar o login: " + ex.Message;
        }
    }

    private async Task<string?> EnsureLoginUrlAsync()
    {
        if (!string.IsNullOrEmpty(_loginUrlCache))
        {
            LoginUrlText = _loginUrlCache;
            return _loginUrlCache;
        }
        try
        {
            LoginBusy = true;
            await _loginApi.FetchPublicKeyAsync();
            var res = await _loginApi.GetLoginUrlAsync();
            if (!res.Success || res.Content == null)
            {
                LoginUrlText = "";
                return null;
            }
            string url = res.Content.ToString()!;
            if (!url.Contains("lenovoid.lang=")) url += "&lenovoid.lang=pt_BR";
            _loginUrlCache = url;
            LoginUrlText = url;
            return url;
        }
        catch
        {
            return null;
        }
        finally
        {
            LoginBusy = false;
        }
    }

    [RelayCommand]
    private async Task ValidateLoginAsync()
    {
        string pasted = LoginPastedText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(pasted))
        {
            LoginStatusText = "Cole a URL da página \"lenovoIdSuccess\" (ou apenas o código wust) no campo acima.";
            return;
        }
        try
        {
            LoginBusy = true;
            LoginStatusText = "Validando login com os servidores Lenovo...";
            await _loginApi.FetchPublicKeyAsync();
            var result = await _loginApi.LoginWithWustAsync(pasted);
            if (result.Success)
            {
                LoginSuccess = true;
                LoginUserName = result.Content?["name"]?.ToString()
                    ?? result.Content?["email"]?.ToString()
                    ?? "(conta conectada)";
                LoginStatusText = "Login validado com sucesso! Sessão salva — o catálogo oficial completo será carregado.";
                AddLog($"[Login] Sessão salva para: {LoginUserName}");
                // com sessão válida, o catálogo oficial completo (linha Brasil) é carregado
                await LoadOfficialCatalogAsync();
            }
            else
            {
                LoginStatusText = $"Falha ao validar: {result.Code} — {result.Desc}. Confira se copiou a URL COMPLETA da página de sucesso.";
                AddLog($"[Login] Falha na validação: {result.Code} {result.Desc}");
            }
        }
        catch (Exception ex)
        {
            LoginStatusText = "Erro na validação: " + ex.Message;
        }
        finally
        {
            LoginBusy = false;
        }
    }

    /// <summary>Abre a tela de login automaticamente quando não há sessão salva.</summary>
    internal void AutoOpenLoginIfNeeded()
    {
        if (_loginApi.TryLoadSession()) return; // já tem sessão salva — não interrompe
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsLoginOpen)
            {
                IsLoginOpen = true;
                LoginStatusText = "Passo 1 — clique em \"Abrir página de login no navegador\".";
                _ = EnsureLoginUrlAsync();
            }
        });
    }
}
