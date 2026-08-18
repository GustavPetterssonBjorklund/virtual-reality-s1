# Planering — Table Tennis VR

> Denna plan följer kursens kriterier i [kursens README](https://github.com/abbjetmus/Virtual-Reality/blob/main/README.md). Planeringen ska godkännas av läraren innan kodning börjar.

## 1. Gruppen

| Namn | Klass | Huvudansvar |
| --- | --- | --- |
| Isak | 240s | XR, handkontroller och nätverk |
| Gustav | 240s | Spelmekanik och fysik |
| Lulia | 240s | 3D-miljö, UI och grafik |
| Robin | 240s | Poäng, ljud, testning och bygge |

**Projektnamn:** Table Tennis VR<br>
**Sport:** Bordtennis<br>
**GitHub-repo:** <https://github.com/abbindustrigymnasium/virtual-reality-s1><br>
**Classroom:** <https://classroom-app.cloud.mustini.com/join/71b740691560cdacfcee>

## 2. Spelidé

**Vilken sport har ni valt?**

Bordtennis.

**Beskriv spelet i 3–5 meningar. Vad gör spelaren? Vad är målet?**

Table Tennis VR är ett VR-sportspel för Meta Quest där två spelare möts vid ett virtuellt bordtennisbord. Varje spelare använder ett headset och en VR-racket för att slå bollen över nätet. Spelet räknar poäng enligt förenklade bordtennisregler och matchen vinns av den första spelaren som når 11 poäng med minst två poängs ledning. Den obligatoriska godkända versionen ska kunna spelas på ett headset; stöd för två headset testas som utökad funktion.

**Varför är den här sporten lämplig i VR?**

Spelaren kan använda riktiga handrörelser, se bollen i tre dimensioner och påverka bollens riktning med racketens vinkel och hastighet. Spelaren står stilla, vilket gör upplevelsen bekväm och minskar risken för åksjuka.

**Vad gör ert spel roligt? (Vad är "kroken"?)**

Timing, precision och känslan av att lyckas returnera en snabb boll. Bollens riktning och hastighet påverkas av hur spelaren träffar den.

Spelet är helt VR-baserat. AR och mixed reality ingår inte.

## 3. Skisser

Följande skisser skapas innan utvecklingen börjar och sparas i `Dokumentation/`:

- [ ] Översikt av spelplanen med bord, nät, spelare och säkra spelarzoner
- [ ] Förstapersonsvy från headsetet
- [ ] Startmeny och lobby
- [ ] Poängtavla/HUD
- [ ] Game over-skärm
- [ ] Flödesschema: meny → anslutning → match → resultat → omstart

**Länkar till bilder:** Fylls i när skisserna är fotograferade och inlagda.

## 4. Spelmekanik

**Vad kan spelaren göra med händerna?**

Spelaren använder handkontrollerna för att greppa och släppa racketen med `XRGrabInteractable`, serva och slå bollen. Handkontrollerna ska synas och följa spelarens händer.

**Vilka fysikobjekt finns och hur beter de sig?**

| Objekt | Rigidbody? | Collider | Beteende |
| --- | --- | --- | --- |
| Bord | Nej | Box Collider | Statisk spelplan i korrekt skala |
| Nät | Nej | Box Collider | Hindrar bollen från att passera under nätet |
| Racket | Ja | Box Collider | Greppas och följer handen |
| Boll | Ja | Sphere Collider | Påverkas av gravitation och racketträff |
| Spelarzoner | Nej | Trigger Collider | Kontrollerar vilken sida av bordet bollen landar på |

Bordet byggs i realistisk skala: 2,74 meter långt, 1,525 meter brett och 0,76 meter högt. En Unity-enhet motsvarar en meter. All Rigidbody-rörelse och bollfysik hanteras i `FixedUpdate()`. Träffar registreras med collision detection och rätt taggar, exempelvis `Boll` och `Racket`.

**Hur räknas poäng? Skriv reglerna tydligt.**

- Bollen ska passera nätet.
- Bollen får studsa högst en gång på varje sida.
- En spelare får poäng när motståndaren missar eller när bollen studsar fel.
- Vid poäng återställs bollen till serveposition.
- Vid 10–10 fortsätter matchen tills någon leder med två poäng.

**Hur vinner eller förlorar man?**

Matchen vinns av den första spelaren som når 11 poäng med minst två poängs ledning. Den andra spelaren förlorar. Resultatet visas på game over-skärmen.

**Hur startas spelet om?**

Spelaren trycker på `Spela igen` på game over-skärmen. Poäng, boll, racketar och matchstatus återställs utan att spelet stängs.

## 5. Kravlista

### Måste (grundkrav)

- [ ] Planeringen är godkänd av läraren innan kodning
- [ ] Unity 6 LTS, URP och OpenXR är konfigurerade
- [ ] XR Origin med fungerande head tracking
- [ ] Synliga handkontroller som följer spelarens händer
- [ ] Spelaren kan greppa och släppa racketen med `XRGrabInteractable`
- [ ] Realistisk bord- och spelplansskala
- [ ] Boll med Rigidbody och Collider
- [ ] Träffdetektion mellan racket, boll och bord
- [ ] Tydligt regelsystem och poängsystem
- [ ] Vinst-/förlustvillkor
- [ ] World Space-startmeny med fungerande `Starta`-knapp
- [ ] `Tracked Device Graphic Raycaster` för VR-knappar
- [ ] Poängtavla/HUD med poäng och matchstatus
- [ ] Game over-skärm med resultat
- [ ] Meny → spel → resultat som scenflöde
- [ ] Omstart/reset av poäng, boll och spelpositioner
- [ ] Ljudeffekter vid träff, studs, serve och poäng
- [ ] Bakgrundsljud eller musik
- [ ] Haptisk feedback vid grepp och bollträff
- [ ] Spatialt 3D-ljud där det passar
- [ ] Enkel 3D-miljö med bord, nät, racket och boll
- [ ] Material och texturer som ger variation
- [ ] VR-optimerad ljussättning, helst baked lighting
- [ ] Stabil bildfrekvens på Meta Quest
- [ ] Ingen påtvingad kamerarörelse
- [ ] Test med minst en person utanför gruppen, dokumenterat i `Dokumentation/`
- [ ] Alla fyra gruppmedlemmar har synliga commits
- [ ] Branches och pull requests används
- [ ] C#-koden är strukturerad och kommenterad
- [ ] Fristående Meta Quest `.apk` som kan köras utan Unity Editor

### Om vi hinner (extra)

- [ ] Träningsläge
- [ ] Highscore
- [ ] Flera arenor
- [ ] Anpassningsbara racketar
- [ ] Stöd för match mellan två Meta Quest-headset

## 6. Arbetsfördelning

| Område | Ansvarig | Beskrivning |
| --- | --- | --- |
| VR-uppsättning och interaktion | Isak | XR Origin, OpenXR, handkontroller och grepp |
| Nätverk | Isak | Test av anslutning och synkronisering som extrafunktion |
| Spelmekanik och fysik | Gustav | Racket, boll, studs, träffvinkel och fysik |
| Poängsystem och regler | Robin | Poäng, matchregler, vinstvillkor och reset |
| 3D-modeller och miljö | Lulia | Bord, nät, racket, boll, material och ljussättning |
| UI och menyer | Lulia | World Space Canvas, startmeny, HUD och game over |
| Ljud och haptik | Robin | Träffljud, studs, poäng, musik och vibration |
| Testning och bygge | Robin | Testprotokoll, prestanda, buggar och APK |
| Dokumentation och skisser | Hela gruppen | Skisser, källor, licenser och slutrapport |

**Hur samarbetar ni i Git?**

Alla kör Git LFS. `.gitignore` och `.gitattributes` läggs till direkt. `Library/`, `Temp/` och `Build/` committas inte, men `.meta`-filer committas alltid. Alla arbetar i egna branches och gör pull requests. Endast en person äger och redigerar huvudscenen åt gången. Funktioner byggs som prefabs där det är möjligt. Alla hämtar senaste versionen innan arbete påbörjas och pushar efter varje arbetstillfälle.

## 7. Tidsplan

Projektperioden är 2026-08-18 till 2026-09-11 och omfattar cirka 30,25 timmar.

| Period | Mål — vad ska vara klart? | Ansvarig |
| --- | --- | --- |
| 18–19 aug | Planering, skisser, Unity 6 LTS, Git LFS och lärargodkännande | Hela gruppen |
| 20–24 aug | XR Origin, head tracking, handkontroller, bord i korrekt skala och racket-prefab | Isak och Lulia |
| 25–27 aug | Boll-Rigidbody, colliders, fysik, träffdetektion och poängregler | Gustav och Robin |
| 28 aug–1 sep | World Space-meny, startknapp, HUD, game over och reset | Lulia och Robin |
| 2–4 sep | Ljud, haptik, spatialt ljud, baked lighting och miljö | Robin och Lulia |
| 5–7 sep | Prestanda, komfort, extern användartestning och buggfixar | Hela gruppen |
| 8–10 sep | Android, IL2CPP, ARM64, ASTC, APK och slutkontroll | Isak och Robin |
| 11 sep | Mässdemo, dokumentation och redovisning | Hela gruppen |

**Deadline för första spelbara version (prototyp):** 24 augusti 2026<br>
**Deadline för färdigt bygge:** 10 september 2026<br>
**Redovisning:** 11 september 2026

## 8. Assets

| Asset | Egen eller hämtad? | Källa / licens |
| --- | --- | --- |
| Bord och nät | Egenbyggt i Unity | Skapat av gruppen |
| Racket och boll | Egenbyggt i Unity | Skapat av gruppen |
| UI | Egenbyggt | Skapat av gruppen |
| Texturer/material | Egenbyggda eller hämtade | Källa och licens dokumenteras |
| Ljud | Egeninspelade eller hämtade | Kenney/Freesound eller annan tillåten licens dokumenteras |

Ingen färdig spelprojektlösning eller färdig spelkod hämtas från nätet. Speldesign, C#-kod och integration skrivs av gruppen.

## 9. Risker

| Risk | Plan B |
| --- | --- |
| Nätverk mellan två headset tar för lång tid | Leverera en komplett lokal enspelarversion som uppfyller alla obligatoriska krav |
| Bollfysiken känns fel | Justera Rigidbody, mass, drag, studs och Throw Velocity Scale genom headsettester |
| Dålig prestanda | Low-poly-modeller, URP, baked lighting, färre ljuskällor och enklare material |
| VR orsakar obehag | Spelaren står stilla och ingen automatisk kamerarörelse används |
| UI är svårt att trycka på | Större World Space-knappar och tydligare raycaster |
| Merge-konflikter i Unity-scener | En scenägare åt gången, prefab-baserat arbete och pull requests |
| APK-bygget misslyckas | Testa Android-bygge senast vecka 2 och skapa APK-versioner löpande |
| För få timmar | Prioritera alla punkter under Måste före nätverk och extrafunktioner |

## 10. Testplan

Följande ska testas och dokumenteras före inlämning:

1. APK:n startar utan Unity Editor.
2. Head tracking, handkontroller och grepp fungerar.
3. Racketen träffar bollen och bollen studsar korrekt.
4. Poäng räknas korrekt vid miss, fel studs och poäng efter serve.
5. Vinst vid 11 poäng och tvåpoängsskillnad fungerar, inklusive 10–10.
6. Meny, scenövergång, HUD, game over och omstart fungerar.
7. Ljud, haptik och spatialt ljud fungerar.
8. Bildfrekvensen är stabil och ingen påtvingad kamerarörelse finns.
9. En extern testperson provar spelet och lämnar dokumenterad feedback.
10. Alla fyra har commits, branches och pull requests i repot.
11. Alla externa assets har dokumenterad källa och licens.
12. Android-inställningarna är IL2CPP, ARM64 och ASTC.

Spelet betraktas som färdigt först när samtliga punkter under **Måste** är testade och markerade.

## 11. Lärarens godkännande

- [ ] Planeringen är godkänd

**Datum:**
**Signatur:**

**Kommentarer från läraren:**
