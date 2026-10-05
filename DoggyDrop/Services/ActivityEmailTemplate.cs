using System.Text.Encodings.Web;
using DoggyDrop.Models;

namespace DoggyDrop.Services;

public sealed record RenderedEmail(string Subject, string Html, string Text);

public sealed class ActivityEmailTemplate(SeoSite site)
{
    public RenderedEmail Render(NotificationOutbox item, string? binName, bool publicBin)
    {
        if (item.PayloadVersion != 1) throw new InvalidOperationException("Unsupported email payload version.");
        var (heading, body) = item.Type switch {
            ActivityEmailType.BinApproved => ("Tvoj predlog koša je odobren", "Hvala! Tvoj predlog koša smo odobrili."),
            ActivityEmailType.BinRejected => ("Tvoj predlog koša je pregledan", "Tokrat predloga koša nismo odobrili. Hvala, ker pomagaš izboljševati DoggyDrop."),
            ActivityEmailType.PhotoApproved => ("Tvoja fotografija je odobrena", "Hvala! Tvojo fotografijo smo odobrili za prikaz ob košu."),
            ActivityEmailType.PhotoRejected => ("Tvoja fotografija je pregledana", "Tokrat fotografije nismo odobrili. Hvala za prispevek."),
            ActivityEmailType.LocationApproved => ("Tvoj predlog lokacije je odobren", "Hvala! Tvoj popravek lokacije smo odobrili."),
            ActivityEmailType.LocationRejected => ("Tvoj predlog lokacije je pregledan", "Tokrat predloga lokacije nismo odobrili. Hvala za pomoč."),
            ActivityEmailType.IssueApproved => ("Tvoja prijava je pregledana", "Tvojo prijavo smo pregledali in sprejeli. To samo po sebi ne pomeni, da je težava odpravljena."),
            ActivityEmailType.IssueRejected => ("Tvoja prijava je pregledana", "Tokrat prijave nismo sprejeli. Hvala, ker pomagaš izboljševati podatke."),
            _ => throw new InvalidOperationException("Unsupported email type.")
        };
        var showMap = publicBin && item.BinId > 0 && item.Type is ActivityEmailType.BinApproved or ActivityEmailType.PhotoApproved or ActivityEmailType.LocationApproved;
        var target = site.Absolute(showMap ? $"/?binId={item.BinId}" : item.Type is ActivityEmailType.BinApproved or ActivityEmailType.BinRejected ? "/Map/MyBins" : "/BinContributions/Mine");
        var label = showMap ? "Poglej na zemljevidu" : item.Type is ActivityEmailType.BinApproved or ActivityEmailType.BinRejected ? "Moji koši" : "Moji prispevki";
        var name = string.IsNullOrWhiteSpace(binName) ? "" : new string(binName.Take(200).Where(c => !char.IsControl(c)).ToArray());
        var preference = site.Absolute("/NotificationPreferences");
        var privacy = site.Absolute("/Home/Privacy");
        static string E(string value) => HtmlEncoder.Default.Encode(value);
        var html = $"""
            <!doctype html><html lang="sl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{E(heading)}</title></head>
            <body style="margin:0;background:#f4f7f4;color:#183e35;font-family:Arial,sans-serif">
            <table role="presentation" style="width:100%;border-collapse:collapse"><tr><td style="padding:20px 12px">
            <table role="presentation" style="width:100%;max-width:600px;margin:auto;background:#ffffff;border-collapse:collapse"><tr><td style="padding:24px;overflow-wrap:anywhere">
            <p style="font-weight:bold;font-size:20px">DoggyDrop</p><h1 style="font-size:24px;line-height:1.3">{E(heading)}</h1>
            <p style="line-height:1.6">{E(body)}</p><p>{E(name)}</p>
            <p><a href="{E(target)}" style="display:inline-block;padding:14px 20px;background:#236957;color:#ffffff;border-radius:8px;text-decoration:underline">{E(label)}</a></p>
            <p style="font-size:14px;line-height:1.6">To obvestilo se nanaša na tvoj prispevek. <a href="{E(preference)}">Nastavitve e-poštnih obvestil</a></p>
            <p style="font-size:14px"><a href="{E(site.Absolute("/"))}">DoggyDrop</a> · <a href="{E(privacy)}">Zasebnost</a></p>
            </td></tr></table></td></tr></table></body></html>
            """;
        return new(heading, html, $"DoggyDrop\n\n{heading}\n\n{body}\n{name}\n\n{label}: {target}\n\nNastavitve e-poštnih obvestil: {preference}\nZasebnost: {privacy}");
    }
}
