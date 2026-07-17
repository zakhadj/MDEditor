Voici le prompt complet, prêt à coller dans une session Claude Code pour démarrer l'implémentation réelle de l'application WPF :

```markdown
# Projet : MD Editor — éditeur Markdown de bureau (C# / WPF)

## Contexte
Créer une application Windows de bureau permettant d'éditer des fichiers Markdown (.md) avec gestion multi-onglets, aperçu en temps réel, et une interface moderne au style Windows 11. Un mockup HTML statique de référence existe (`md_editor_mockup.html`) : il illustre la disposition générale, le style visuel (Windows 11, coins arrondis, barre de titre personnalisée) et certains comportements interactifs (glisser-déposer, menus contextuels). Utilise-le comme référence visuelle, mais l'implémentation finale doit être une vraie application WPF native — pas un wrapper web/Electron.

## Stack technique imposée
- **.NET 8** (ou la version LTS la plus récente disponible), application **WPF**
- **AvalonEdit** (`ICSharpCode.AvalonEdit`) pour l'éditeur de texte brut (panneau gauche)
- **WebView2** (`Microsoft.Web.WebView2`) pour afficher le rendu HTML du Markdown (panneau droit)
- **Markdig** pour la conversion Markdown → HTML
- Architecture recommandée : MVVM (ViewModels pour la fenêtre principale, les onglets, les boîtes de dialogue)

## Fonctionnalités détaillées

### 1. Fenêtre principale et thème visuel
- Style moderne Windows 11 (coins arrondis, palette neutre, Fluent-like)
- Barre de titre personnalisée (non standard Windows) contenant : icône + nom de l'application **"MD Editor"** (jamais le nom du fichier), et les trois boutons système : réduire, agrandir/restaurer, fermer
- Bouton de bascule clair/sombre dans la barre de titre — **mode clair actif par défaut**, le thème choisi doit s'appliquer à toute l'interface (menus, onglets, panneaux, boîtes de dialogue)

### 2. Gestion multi-onglets
- Un onglet par fichier ouvert, affichant le nom du fichier, avec un bouton de fermeture par onglet
- Bouton "+" pour créer un nouvel onglet vide
- Cliquer sur un onglet l'active et affiche son contenu dans les deux panneaux (brut / rendu)
- Fermer un onglet contenant des modifications non enregistrées doit demander confirmation

### 3. Sauvegarde automatique et restauration de session
- Chaque onglet est sauvegardé automatiquement (debounce ~1-2s après la dernière frappe) dans un fichier sous `%TEMP%\MdEditor\autosave\`
- Un fichier manifeste de session (ex. `%TEMP%\MdEditor\session.json`) enregistre pour chaque onglet : identifiant, nom affiché, chemin du fichier d'origine (ou `null` si jamais enregistré sur disque), chemin du fichier autosave, et quel onglet est actif
- **Au démarrage de l'application**, relire ce manifeste et rouvrir automatiquement tous les onglets de la session précédente, en chargeant le contenu depuis la copie autosave (pour ne perdre aucune modification non enregistrée), tout en conservant le lien vers le fichier d'origine pour les sauvegardes futures (Ctrl+S doit écrire au bon endroit)

### 4. Édition (panneau gauche) et rendu (panneau droit)
- Panneau gauche : éditeur de texte brut (AvalonEdit) affichant la syntaxe Markdown
- Panneau droit : rendu HTML en temps réel du contenu via Markdig + WebView2, mis à jour à chaque modification du texte brut

### 5. Barre d'outils de mise en forme
Boutons insérant directement la syntaxe Markdown dans le panneau gauche (au niveau du curseur ou en enveloppant la sélection) :
- Titre 1 (`# `), Titre 2 (`## `), Titre 3 (`### `)
- Gras (`**texte**`), Italique (`*texte*`)
- Liste à puces (`- `), Liste numérotée (`1. `)
- Lien (`[texte](url)`), Image (`![alt](url)`)
- Bloc de code (`` ``` ``), Citation (`> `), Tableau (squelette de tableau Markdown)

### 6. Synchronisation défilement / sélection
- Option (case à cocher, persistée) : synchroniser le défilement entre les deux panneaux — faire défiler l'un fait défiler l'autre proportionnellement
- Option (case à cocher, persistée) : synchroniser la sélection — sélectionner du texte dans un panneau sélectionne/surligne le passage correspondant dans l'autre panneau

### 7. Thème clair / sombre
- Bascule accessible depuis la barre de titre, mode clair par défaut
- Préférence persistée entre les sessions (ex. `%AppData%\MdEditor\settings.json`)

### 8. Menu "Fichier"
- Nouveau
- Ouvrir
- Sauvegarder (Ctrl+S)
- Sauvegarder sous...
- Fermer (ferme l'onglet actif)
- Fermer tous les onglets
- Imprimer (imprime le rendu HTML de l'onglet actif, via les capacités d'impression de WebView2)
- Liste des 10 derniers fichiers ouverts (persistée, mise à jour à chaque ouverture/sauvegarde ; cliquer sur une entrée ouvre le fichier dans un nouvel onglet)

### 9. Menu "Edition"
- Annuler (Ctrl+Z)
- Rétablir (Ctrl+Y)
- ─────────
- Couper (Ctrl+X)
- Copier (Ctrl+C)
- Coller (Ctrl+V)
- Tout sélectionner (Ctrl+A)

Annuler/Rétablir s'appliquent à la pile undo/redo de l'éditeur (AvalonEdit). Couper n'est actif que lorsque le focus est sur la zone d'édition brute (la zone de rendu est en lecture seule).

### 10. Menu "Recherche"
- **Chercher** (Ctrl+F) : boîte de dialogue avec un champ de recherche, un bouton "Rechercher dans l'onglet actif", un bouton "Rechercher dans tous les onglets" (parcourt les onglets ouverts et bascule automatiquement vers celui contenant l'occurrence suivante), et une case "Respecter la casse" **décochée par défaut**
- **Remplacer** (Ctrl+H) : boîte de dialogue avec un champ de recherche, un champ de remplacement, un bouton "Remplacer dans l'onglet actif", un bouton "Remplacer dans tous les onglets ouverts", et une case "Respecter la casse" **décochée par défaut**

### 11. Menu "?"
- **A propos de MD Editor** : boîte de dialogue affichant :
  - La version : `Version 1.0`
  - Le texte de licence suivant (GNU GPL v3, en français) :

    > Ce programme est un logiciel libre ; vous pouvez le redistribuer et/ou le modifier au titre des clauses de la Licence Publique Générale GNU, telle que publiée par la Free Software Foundation ; soit la version 3 de la Licence, ou (à votre discrétion) une version ultérieure quelconque.
    >
    > Ce programme est distribué dans l'espoir qu'il sera utile, mais SANS AUCUNE GARANTIE ; sans même la garantie implicite de COMMERCIALISABILITÉ ou DE CONFORMITÉ À UNE UTILISATION PARTICULIÈRE. Voir la Licence Publique Générale GNU pour plus de détails.
    >
    > Vous devriez avoir reçu un exemplaire de la Licence Publique Générale GNU avec ce programme ; si ce n'est pas le cas, consultez <https://www.gnu.org/licenses/>.
  - En bas à gauche de la boîte de dialogue : `Zakaria HADJ, 2026`

### 12. Glisser-déposer (drag and drop)
- Glisser un fichier `.md`/`.markdown` n'importe où sur la fenêtre principale → ouverture dans un nouvel onglet, qui devient l'onglet actif, contenu chargé et rendu généré
- Glisser un fichier `.txt` → même comportement (ouverture dans un nouvel onglet)
- Glisser tout autre type de fichier → afficher l'erreur **"Formats supportés : .txt et .MD"** (sans créer d'onglet), avec un retour visuel clair (ex. bandeau d'erreur temporaire) plutôt qu'une simple MessageBox bloquante
- Un retour visuel pendant le survol (overlay ou changement de style de la zone d'édition) doit indiquer que le dépôt est possible

### 13. Menus contextuels (clic droit)
- Clic droit sur le panneau **gauche** (texte brut) → menu contextuel : Couper, Copier, Coller, Tout sélectionner — agissant sur la sélection réelle de l'éditeur
- Clic droit sur le panneau **droit** (rendu) → menu contextuel : Copier, Coller, Tout sélectionner
  - Copier copie la sélection visible dans le rendu
  - Coller insère le contenu du presse-papiers dans le document source (panneau gauche), puisque le rendu est en lecture seule
  - Tout sélectionner sélectionne l'intégralité du contenu affiché dans le rendu
- Ces menus contextuels remplacent le menu contextuel natif sur ces deux zones

## Structure de projet suggérée
```
MdEditor.sln
└─ MdEditor/
   ├─ App.xaml / App.xaml.cs
   ├─ MainWindow.xaml / MainWindow.xaml.cs
   ├─ Views/            (FindReplaceDialog, AboutDialog, ...)
   ├─ ViewModels/        (MainViewModel, DocumentTabViewModel, ...)
   ├─ Services/          (FileService, SessionService, RecentFilesService, MarkdownRenderService, SearchService)
   ├─ Models/            (DocumentTab, AppSettings)
   ├─ Themes/            (Light.xaml, Dark.xaml)
   └─ Resources/
```

## Critères d'acceptation
- Démarrage à vide (premier lancement) → un onglet vide par défaut
- Ouvrir 3 fichiers, fermer l'application, la relancer → les 3 onglets se restaurent avec leur contenu exact, y compris les modifications non enregistrées
- Modifier du texte sans sauvegarder, fermer puis rouvrir l'app → les modifications sont conservées (preuve de l'autosave dans `%TEMP%`)
- Glisser un `.md`, un `.txt`, puis un `.png` → comportements respectifs corrects (ouverture, ouverture, message d'erreur)
- Basculer clair/sombre → toute l'UI suit le thème, le mode clair est actif au premier lancement
- Activer/désactiver la synchronisation du défilement et de la sélection → comportement conforme dans les deux cas
- Chercher/Remplacer avec et sans respect de la casse, sur l'onglet actif et sur tous les onglets ouverts
- Menu Fichier > liste des 10 derniers fichiers se met à jour après plusieurs ouvertures
- Impression du rendu via Fichier > Imprimer
- A propos affiche la version 1.0, le texte de licence GPL en français, et "Zakaria HADJ, 2026" en bas à gauche
- Clic droit sur chaque panneau affiche le bon menu contextuel avec les bonnes actions fonctionnelles
```