using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 41 in Settings: Elsa's request body, reply path and token header, typed when Elsa's team gives them, so no source edit is needed.
public partial class SettingsWindow
{
    private void BuildElsa()
    {
        ElsaHint.Text = ElsaMapping.Hint;
        ElsaHeader.Text = _settings.ElsaAuthHeader; ElsaScheme.Text = _settings.ElsaAuthScheme;
        ElsaTemplate.Text = _settings.ElsaRequestTemplate; ElsaPath.Text = _settings.ElsaReplyPath;
        void Save()
        {
            _settings.ElsaAuthHeader = (ElsaHeader.Text ?? "").Trim(); _settings.ElsaAuthScheme = (ElsaScheme.Text ?? "").Trim();
            _settings.ElsaRequestTemplate = ElsaTemplate.Text ?? ""; _settings.ElsaReplyPath = (ElsaPath.Text ?? "").Trim();
            Apply(); SayElsa();
            ConnStatus.Text = ConnectionService.DescribeFor(_settings);
        }
        foreach (var box in new[] { ElsaHeader, ElsaScheme, ElsaTemplate, ElsaPath }) box.LostFocus += (_, _) => Save();
        SayElsa();
    }

    private void SayElsa() => ElsaStatusLine.Text = ElsaMapping.Problem(_settings)
        ?? (ElsaMapping.For(_settings).IsFilledIn ? ElsaMapping.SaidFilled : ElsaMapping.SaidBlank);
}
