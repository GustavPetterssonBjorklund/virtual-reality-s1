# Planering — VR-sportspel i Unity

> **Obligatorisk.** Fyll i hela dokumentet och lämna in det till läraren för godkännande **innan** ni börjar koda.
> Ta bort exempeltexten i kursivt och skriv era egna svar.

---

## 1. Gruppen

| Namn | Klass | Huvudansvar |
| ---- | ----- | ----------- |
|      |       |             |
|      |       |             |
|      |       |             |
|      |       |             |

**Projektnamn:**

**GitHub-repo (länk):**

---

## 2. Spelidé

**Vilken sport har ni valt?**

_Exempel: Bowling._

**Beskriv spelet i 3–5 meningar. Vad gör spelaren? Vad är målet?**

_Exempel: Spelaren står vid en bowlinglinje i en bowlinghall. Man plockar upp klotet med handkontrollen och kastar det mot tio käglor. Spelet räknar antal nedslagna käglor per kast och visar poängen på en tavla ovanför banan. Efter 10 rutor visas slutresultatet._

**Varför är den här sporten lämplig i VR?**

**Vad gör ert spel roligt? (Vad är "kroken"?)**

---

## 3. Skisser

Rita spelplanen, var spelaren står, vad hen ser och hur UI:t ser ut. Handritat och fotograferat går bra — lägg bilderna i mappen `Dokumentation/` och länka till dem här.

- [ ] Skiss: översikt av spelplanen
- [ ] Skiss: vad spelaren ser i headsetet (förstapersonsvy)
- [ ] Skiss: startmeny och poängtavla

_Länkar till bilder:_

---

## 4. Spelmekanik

**Vad kan spelaren göra med händerna?**

_Exempel: Greppa klotet med greppknappen, svinga armen och släppa för att kasta._

**Vilka fysikobjekt finns och hur beter de sig?**

| Objekt  | Rigidbody? | Collider  | Beteende                          |
| ------- | ---------- | --------- | --------------------------------- |
| _Klot_  | _Ja_       | _Sphere_  | _Rullar, påverkas av gravitation_ |
| _Kägla_ | _Ja_       | _Capsule_ | _Välter vid träff_                |
|         |            |           |                                   |

**Hur räknas poäng? Skriv reglerna tydligt.**

**Hur vinner eller förlorar man?**

**Hur startas spelet om?**

---

## 5. Kravlista

Dela upp funktionerna. **Måste** ska vara klart för godkänt — börja alltid med dem.

### Måste (grundkrav)

- [ ] VR-spelare (XR Origin) med fungerande head tracking
- [ ] Spelaren kan greppa och släppa minst ett föremål
- [ ] Träffdetektion som fungerar
- [ ] Poängsystem
- [ ] Vinst-/förlustvillkor
- [ ] Startmeny i World Space
- [ ] Game over-skärm med resultat
- [ ] Ljudeffekter
- [ ] Omstart av spelet
- [ ] Byggbar .apk
- [ ]
- [ ]

### Om vi hinner (extra)

- [ ] Haptisk feedback
- [ ] Bakgrundsmusik / publikljud
- [ ] Highscore som sparas
- [ ] Svårighetsgrader
- [ ] Flera banor / nivåer
- [ ]
- [ ]

---

## 6. Arbetsfördelning

| Område                                  | Ansvarig | Beskrivning |
| --------------------------------------- | -------- | ----------- |
| VR-uppsättning (XR Origin, interaktion) |          |             |
| Spelmekanik och fysik                   |          |             |
| Poängsystem och regler                  |          |             |
| 3D-modeller och miljö                   |          |             |
| UI och menyer                           |          |             |
| Ljud                                    |          |             |
| Testning och bygge                      |          |             |

**Hur samarbetar ni i Git?** _(branches, vem äger scenen, hur ofta ni pushar)_

---

## 7. Tidsplan

| Vecka | Mål — vad ska vara klart?                                       | Ansvarig |
| ----- | --------------------------------------------------------------- | -------- |
| 1     | _Unity + VR installerat, tutorial genomförd, planering godkänd_ |          |
| 2     |                                                                 |          |
| 3     |                                                                 |          |
| 4     |                                                                 |          |
| 5     |                                                                 |          |
| 6     |                                                                 |          |

**Deadline för första spelbara version (prototyp):**

**Deadline för färdigt bygge:**

---

## 8. Assets

Vad bygger ni själva och vad hämtar ni? Ange alltid källa och licens.

| Asset | Egen eller hämtad? | Källa / licens |
| ----- | ------------------ | -------------- |
|       |                    |                |
|       |                    |                |
|       |                    |                |

---

## 9. Risker

Vad kan gå fel och vad gör ni då?

| Risk                                  | Plan B                                                              |
| ------------------------------------- | ------------------------------------------------------------------- |
| _Kastet känns fel i VR_               | _Testa Throw Velocity Scale tidigt, be någon utanför gruppen testa_ |
| _Vi hinner inte med 10 rutor bowling_ | _Börja med ett enda kast och bygg ut_                               |
|                                       |                                                                     |

---

## 10. Lärarens godkännande

- [ ] Planeringen är godkänd

**Datum:** **Signatur:**

**Kommentarer från läraren:**
