# MD Editor

Éditeur Markdown de bureau pour Windows, avec aperçu en temps réel et gestion multi-onglets.

![Plateforme](https://img.shields.io/badge/plateforme-Windows%2010%2F11-0c447c) ![.NET](https://img.shields.io/badge/.NET-8-512bd4) ![Licence](https://img.shields.io/badge/licence-GPLv3-791f1f)

## Fonctionnalités

### Édition et aperçu
- Panneau gauche : éditeur de texte brut Markdown (coloration syntaxique de base, retour à la ligne automatique)
- Panneau droit : rendu HTML du Markdown en temps réel, mis à jour à chaque modification
- Synchronisation optionnelle du défilement entre les deux panneaux
- Synchronisation optionnelle de la sélection : sélectionner du texte dans un panneau surligne le passage correspondant dans l'autre

### Multi-onglets
- Un onglet par fichier ouvert, avec bouton de fermeture individuel
- Bouton « + » pour créer un nouvel onglet vide
- Confirmation demandée avant de fermer un onglet contenant des modifications non enregistrées

### Sauvegarde automatique et restauration de session
- Chaque onglet est sauvegardé automatiquement quelques secondes après la dernière frappe, dans un fichier temporaire
- Au redémarrage de l'application, tous les onglets de la session précédente sont rouverts automatiquement, **y compris les modifications non enregistrées**
- Le lien vers le fichier d'origine est conservé pour que Ctrl+S enregistre toujours au bon endroit

### Barre d'outils de mise en forme
Insertion directe de syntaxe Markdown au niveau du curseur ou autour de la sélection :

| Bouton | Syntaxe |
|---|---|
| Titre 1 / 2 / 3 | `# `, `## `, `### ` |
| Gras / Italique | `**texte**`, `*texte*` |
| Liste à puces / numérotée | `- `, `1. ` |
| Lien / Image | `[texte](url)`, `![alt](url)` |
| Bloc de code / Citation | `` ``` ``, `> ` |
| Tableau | squelette de tableau Markdown |

### Menu Fichier
Nouveau · Ouvrir · Sauvegarder (Ctrl+S) · Sauvegarder sous… (Ctrl+Shift+S) · Fermer (Ctrl+W) · Fermer tous les onglets · Imprimer · Liste des 10 derniers fichiers ouverts

### Menu Édition
Annuler (Ctrl+Z) · Rétablir (Ctrl+Y) · Couper (Ctrl+X) · Copier (Ctrl+C) · Coller (Ctrl+V) · Tout sélectionner (Ctrl+A)

### Menu Recherche
- **Chercher** (Ctrl+F) : dans l'onglet actif ou dans tous les onglets ouverts (bascule automatiquement vers l'occurrence suivante), avec option « Respecter la casse »
- **Remplacer** (Ctrl+H) : dans l'onglet actif ou dans tous les onglets ouverts, avec option « Respecter la casse »

### Glisser-déposer
- Glisser un fichier `.md`, `.markdown` ou `.txt` sur la fenêtre l'ouvre dans un nouvel onglet
- Tout autre format affiche un message d'erreur temporaire sans créer d'onglet
- Un repère visuel indique pendant le survol que le dépôt est possible

### Menus contextuels (clic droit)
- Panneau gauche (texte brut) : Couper, Copier, Coller, Tout sélectionner
- Panneau droit (rendu) : Copier (la sélection visible), Coller (insère dans le document source), Tout sélectionner

### Thème clair / sombre
Bascule accessible depuis la barre de titre, mode clair actif par défaut. La préférence est conservée entre les sessions, et s'applique à l'ensemble de l'interface, y compris l'aperçu Markdown.


## À propos de MD Editor

**Version 1.2**

Ce programme est un logiciel libre ; vous pouvez le redistribuer et/ou le modifier au titre des clauses de la Licence Publique Générale GNU, telle que publiée par la Free Software Foundation ; soit la version 3 de la Licence, ou (à votre discrétion) une version ultérieure quelconque.

Ce programme est distribué dans l'espoir qu'il sera utile, mais SANS AUCUNE GARANTIE ; sans même la garantie implicite de COMMERCIALISABILITÉ ou DE CONFORMITÉ À UNE UTILISATION PARTICULIÈRE. Voir la Licence Publique Générale GNU pour plus de détails.

Vous devriez avoir reçu un exemplaire de la Licence Publique Générale GNU avec ce programme ; si ce n'est pas le cas, consultez <https://www.gnu.org/licenses/>.

---

Zakaria HADJ, 2026
