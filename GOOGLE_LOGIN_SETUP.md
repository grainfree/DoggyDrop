# DoggyDrop Google Login Setup

Da bo Google prijava dejansko delovala, moraš v Google Cloud Console ustvariti OAuth 2.0 prijavo in vnesti `ClientId` ter `ClientSecret`.

## 1. Google Cloud Console

Odpri:

- [https://console.cloud.google.com/](https://console.cloud.google.com/)

Nato:

1. ustvari ali izberi projekt
2. odpri `APIs & Services`
3. odpri `OAuth consent screen`
4. nastavi ime aplikacije: `DoggyDrop`
5. dodaj testne uporabnike, če je aplikacija še v testni fazi

## 2. Ustvari OAuth Client ID

Pojdi na:

- `APIs & Services` -> `Credentials`
- `Create Credentials` -> `OAuth client ID`

Izberi:

- `Web application`

Predlagano ime:

- `DoggyDrop Local`

## 3. Authorized redirect URIs

Za lokalni razvoj dodaj:

- `http://localhost:5177/signin-google`
- `http://127.0.0.1:5177/signin-google`

Če boš kasneje aplikacijo objavil online, dodaj še:

- `https://tvoja-domena/signin-google`

## 4. Vnos v aplikacijo

Najlažje lokalno nastaviš v `appsettings.Development.json` ali kot environment variables.

### Možnost A: `appsettings.Development.json`

V datoteko:

- `C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\DoggyDrop\appsettings.Development.json`

vstavi:

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "TVOJ_GOOGLE_CLIENT_ID",
      "ClientSecret": "TVOJ_GOOGLE_CLIENT_SECRET"
    }
  }
}
```

### Možnost B: environment variables

Nastavi:

- `GOOGLE_CLIENT_ID`
- `GOOGLE_CLIENT_SECRET`

## 5. Ponovni zagon

Po spremembi ponovno zaženi DoggyDrop.

Ko je Google pravilno nastavljen:

- na ekranu `Prijava` se pokaže Google gumb
- na ekranu `Registracija` se pokaže Google gumb
- ob prvem Google loginu DoggyDrop sam ustvari uporabnika, potrdi email in ga prijavi

## Opomba

Google prijava na telefonu bo najbolj zanesljiva, ko bo aplikacija tekla prek javnega `https` naslova. Lokalni `http://localhost` je dober za razvoj na računalniku.
