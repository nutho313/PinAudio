# Nutho313 PinAudio

**Nutho313 PinAudio** est une application Windows destinée à piloter les volumes des sorties audio d'un pincab ou d'une installation audio multicanale. Elle permet de régler les canaux individuellement ou par groupes, d'associer des raccourcis clavier et des boutons de contrôleur, et d'afficher un overlay de volume directement sur un écran choisi.

- **Développeur :** nutho313
- **Dépôt :** [github.com/nutho313/PinAudio](https://github.com/nutho313/PinAudio)
- **Soutien :** [Ko-fi – nutho313](https://ko-fi.com/nutho313)

---

## Sommaire

- [Fonctionnalités](#fonctionnalités)
- [Interface](#interface)
- [Contrôle des volumes](#contrôle-des-volumes)
- [Audio Mapping](#audio-mapping)
- [Raccourcis clavier et contrôleurs](#raccourcis-clavier-et-contrôleurs)
- [Night Mode](#night-mode)
- [Overlay](#overlay)
- [Paramètres généraux et fichiers de configuration](#paramètres-généraux-et-fichiers-de-configuration)
- [Installation et compilation](#installation-et-compilation)
- [Fichiers complémentaires](#fichiers-complémentaires)
- [Dépannage](#dépannage)
- [Crédits et soutien](#crédits-et-soutien)

---

## Fonctionnalités

- Détection des périphériques audio de lecture Windows actifs.
- Réglage du volume de chaque canal audio via les contrôles de volume Windows.
- Gestion des sorties stéréo et des périphériques multicanaux, dont les périphériques 8 canaux utilisés pour le SSF.
- Association des canaux Windows aux positions physiques du pincab.
- Liaison ou séparation des canaux gauche et droit lorsque la configuration le permet.
- Raccourcis clavier globaux et raccourcis par groupe/canal.
- Affectation de boutons de contrôleurs compatibles avec l'interface joystick Windows, notamment les configurations Pinscape / OpenPinDev exposées comme joystick.
- Sélection cyclique d'une zone audio, puis augmentation ou diminution de son volume.
- Night Mode avec atténuation réglable et restauration des niveaux enregistrés à l'activation.
- Overlay graphique sans bordure, toujours au premier plan, affichable sur un écran choisi et orientable à 0°, 90°, 180° ou 270°.
- Icône de notification Windows et possibilité de réduire l'application dans la zone de notification.

## Interface

L'application comporte quatre onglets.

### 1. Audio Mapping

Cette page associe les canaux audio Windows aux positions des haut-parleurs du pincab.

Les positions proposées sont :

| Zone | Affectation |
|---|---|
| BackGlass | BackGlass L et BackGlass R |
| SSR | SSR L et SSR R |
| L / R | L/R L et L/R R |
| Sub | Sub |
| Center | Center |
| SSF | SSF L et SSF R |

Pour chaque périphérique détecté, les listes déroulantes affichent les canaux disponibles. Une position peut rester sur **Not assigned**.

Les associations sont enregistrées automatiquement dans `audio-mappings.json`.

**Configuration SSF 8 canaux :** lorsqu'un périphérique possède huit canaux, l'application propose initialement cette répartition :

| Canal Windows | Position initiale |
|---:|---|
| 1 (index 0) | L/R L |
| 2 (index 1) | L/R R |
| 3 (index 2) | Center |
| 4 (index 3) | Sub |
| 5 (index 4) | SSR L |
| 6 (index 5) | SSR R |
| 7 (index 6) | SSF L |
| 8 (index 7) | SSF R |

Cette répartition est une valeur initiale : elle doit être vérifiée et adaptée à votre câblage et à votre configuration Windows.

Pour un périphérique stéréo à deux canaux, l'application propose initialement BackGlass L sur le canal 1 et BackGlass R sur le canal 2.

### 2. Audio Setting

Cette page regroupe :

- un schéma du pincab indiquant les affectations audio détectées ;
- l'état Linked / Unlinked des zones stéréo de l'overlay ;
- les raccourcis globaux ;
- le bouton et le réglage de pourcentage du Night Mode.

Le bouton **Linked / Unlinked** du schéma contrôle la manière dont la paire gauche/droite est présentée et pilotée par les commandes globales de sélection. Les associations de canaux physiques se règlent dans **Audio Mapping**.

### 3. Display

Cette page permet de configurer l'affichage de l'overlay :

- sélectionner l'écran Windows en cliquant sur sa représentation dans le schéma ;
- choisir la position : Center, Left, Top, Bottom ou Right ;
- choisir l'orientation : 0°, 90°, 180° ou 270° ;
- régler la durée d'affichage ;
- régler la taille ;
- régler l'opacité.

Cliquer sur **Save** pour enregistrer les paramètres.

### 4. General

Cette page contient les options de démarrage et de notification :

- **Start with Windows** : lancer PinAudio à l'ouverture de session Windows ;
- **Display in the Tray Taskbar** : afficher l'icône dans la zone de notification ;
- **Minimize to Tray on Close** : masquer la fenêtre dans la zone de notification au lieu de quitter lorsque l'on ferme la fenêtre.

Si la réduction dans la zone de notification est activée, utiliser **Exit** dans le menu de l'icône de notification pour quitter complètement l'application.

La page affiche également le logo Nutho313 et les liens vers GitHub et Ko-fi.

---

## Contrôle des volumes

Chaque périphérique de lecture Windows actif est affiché avec ses groupes de canaux et ses curseurs.

- Déplacer un curseur modifie le volume du canal correspondant.
- Lorsqu'une paire est liée, le réglage d'un côté applique la même valeur aux deux canaux du groupe.
- Lorsqu'une paire est séparée, chaque canal peut être réglé indépendamment.
- Les valeurs sont appliquées directement au volume de canal exposé par Windows via NAudio.

Pour les périphériques 8 canaux, les groupes initiaux sont :

| Groupe | Canaux (index à partir de 0) |
|---|---|
| SUB | 3 |
| FRONT | 6 et 7 |
| LR | 0 et 1 |
| REAR | 4 et 5 |
| CENTER | 2 |

Les groupes sont initialement liés pour les paires stéréo. Le bouton de liaison de chaque paire permet de basculer entre le réglage commun et le réglage indépendant.

> Les noms et l'ordre réels des canaux dépendent du périphérique, du pilote et de la configuration Windows. Vérifiez les affectations avant utilisation.

---

## Audio Mapping

Le routage ne déplace pas physiquement le son entre périphériques. Il indique à PinAudio quels canaux de quels périphériques correspondent aux zones du pincab, afin que l'interface, les raccourcis et l'overlay pilotent les bons volumes.

Conseils de configuration :

1. Vérifier que les périphériques audio attendus sont actifs dans Windows.
2. Ouvrir **Audio Mapping**.
3. Pour chaque périphérique, choisir le canal Windows correspondant à chaque position.
4. Laisser les positions inutilisées sur **Not assigned**.
5. Vérifier le schéma dans **Audio Setting**.
6. Tester chaque zone avant de configurer les raccourcis.

Les associations sont sauvegardées dans le dossier de paramètres de l'utilisateur Windows.

---

## Raccourcis clavier et contrôleurs

### Raccourcis par groupe

Les raccourcis de chaque groupe se configurent depuis les boutons présents sous les curseurs audio. Cliquer sur le raccourci à modifier, puis appuyer sur la touche ou combinaison souhaitée.

Les combinaisons clavier peuvent inclure Ctrl, Alt et Shift.

Pour les groupes stéréo :

- si la paire est liée, les commandes Volume − et Volume + pilotent les deux canaux ;
- si elle est séparée, chaque canal possède ses commandes propres.

### Raccourcis globaux

Quatre actions globales sont disponibles :

| Action | Fonction |
|---|---|
| Channel Selector | Passer à la prochaine zone de volume sélectionnable |
| Night Mode | Activer ou désactiver le Night Mode |
| Volume Down | Diminuer le volume de la zone sélectionnée |
| Volume Up | Augmenter le volume de la zone sélectionnée |

Les raccourcis globaux ne sont pas préaffectés dans le code : les configurer dans **Audio Setting**.

Le sélecteur parcourt les zones disponibles parmi BackGlass, SSR, L/R, Sub, Center, SSF, puis Night Mode. Une paire séparée peut apparaître sous la forme de deux cibles distinctes, gauche et droite.

Les actions Volume Up et Volume Down modifient la cible sélectionnée par pas de **2 points de pourcentage**.

### Pinscape / OpenPinDev / joystick

L'application interroge les contrôleurs exposés par l'interface joystick Windows. Lors de la capture d'un raccourci, appuyer sur le bouton physique voulu.

L'affectation est associée au numéro de bouton et à l'identifiant du contrôleur détecté. Si plusieurs contrôleurs sont connectés, vérifier que le bon contrôleur est sélectionné.

La lecture des boutons est effectuée périodiquement pendant que l'application fonctionne. Les axes analogiques ne sont pas utilisés pour les raccourcis décrits ici.

---

## Night Mode

Le Night Mode applique une réduction proportionnelle aux volumes des canaux audio.

1. À l'activation, PinAudio mémorise le niveau courant de chaque canal des périphériques détectés.
2. Le pourcentage choisi est appliqué à chaque canal à partir de cette valeur mémorisée.
3. Modifier le curseur de pourcentage recalcule l'atténuation à partir des niveaux de référence, et non à partir du volume déjà atténué.
4. À la désactivation, PinAudio tente de restaurer les niveaux mémorisés.

Le pourcentage est réglable de 0 à 100 %. Le réglage est conservé dans les paramètres de l'application.

**Important :** le Night Mode ne constitue pas un limiteur de sécurité sonore. Le résultat audible dépend des niveaux de départ, des périphériques et de la chaîne audio.

---

## Overlay

L'overlay est une fenêtre graphique sans bordure, affichée au premier plan sur l'écran configuré.

Il présente :

- une représentation du cabinet avec une zone BackGlass et une zone playfield ;
- les niveaux des zones BackGlass, SSR, L/R, SUB, CENTER et SSF lorsqu'elles sont affectées ;
- les niveaux gauche/droite lorsqu'une paire est séparée ;
- la sélection courante ;
- l'état et le pourcentage du Night Mode ;
- les quatre raccourcis globaux et leurs affectations.

L'overlay se met à jour lors des changements de volume et des actions globales. Il se masque automatiquement après la durée définie dans **Display**.

L'écran cible est mémorisé par son nom Windows. Si cet écran n'est plus disponible, l'application utilise un écran de remplacement.

L'orientation fait tourner le contenu de l'overlay. Les dimensions de la fenêtre sont adaptées à l'orientation et à l'espace disponible sur l'écran.

---

## Paramètres généraux et fichiers de configuration

Les paramètres sont stockés dans :

```text
%APPDATA%\AudioOutputController\
```

Fichiers utilisés par l'application :

| Fichier | Contenu |
|---|---|
| `settings.json` | Affectations des raccourcis clavier et boutons joystick |
| `audio-mappings.json` | Association des canaux Windows aux zones du pincab et états de liaison de l'overlay |
| `app-settings.json` | Paramètres généraux, durée/taille/opacité de l'overlay et pourcentage Night Mode |
| `display-settings.json` | Écran cible, position et orientation de l'overlay |

Ces fichiers sont créés ou mis à jour par l'application. Pour sauvegarder votre configuration, fermer PinAudio puis copier le dossier `AudioOutputController`.

---

## Installation et compilation

### Prérequis

Le code fourni utilise :

- Windows ;
- Windows Forms ;
- .NET ;
- la bibliothèque **NAudio** (`NAudio.CoreAudioApi`) pour accéder aux périphériques et volumes audio ;
- les API Windows `RegisterHotKey` pour les raccourcis globaux ;
- `winmm.dll` pour interroger les contrôleurs joystick.

La version exacte du SDK .NET et la version NuGet de NAudio doivent être vérifiées dans les fichiers `.csproj` et de solution du dépôt. Elles ne peuvent pas être déduites du seul fichier `Form1.cs`.

### Compiler depuis le dépôt

1. Installer le SDK .NET correspondant au fichier projet.
2. Cloner le dépôt :

   ```powershell
   git clone https://github.com/nutho313/PinAudio.git
   cd PinAudio
   ```

3. Ouvrir le fichier solution (`.sln`) ou projet (`.csproj`) dans Visual Studio, ou compiler en ligne de commande depuis le dossier qui contient le `.csproj` :

   ```powershell
   dotnet restore
   dotnet build -c Release
   ```

4. Lancer l'application depuis Visual Studio ou depuis les fichiers générés dans `bin\Release`.

Si le dépôt ne contient pas encore de solution ou de projet, ajouter les fichiers de projet nécessaires avant de suivre ces étapes.

---

## Fichiers complémentaires

Le code de l'application recherche certains éléments graphiques dans le dossier de lancement :

| Fichier | Utilisation |
|---|---|
| `nutho313-logo.png` | Logo affiché dans les paramètres et dans l'overlay |
| `Nutho313PinAudio.ico` | Icône de la fenêtre principale |
| `Nutho313PinAudio.Tray.ico` | Icône de la zone de notification |

Ces fichiers doivent être présents à côté de l'exécutable pour que les images et icônes personnalisées soient chargées. En leur absence, le code prévoit une icône Windows générique pour la zone de notification et le logo peut rester vide.

---

## Dépannage

### Aucun périphérique audio n'apparaît

- Vérifier que le périphérique est activé dans les paramètres audio Windows.
- Vérifier que Windows le présente comme périphérique de lecture.
- Cliquer sur **Refresh audio devices** dans Audio Mapping.

### Une zone n'apparaît pas dans l'overlay

Vérifier qu'au moins un canal lui est affecté dans **Audio Mapping**. Les zones sans canal affecté ne sont pas dessinées.

### Un raccourci ne fonctionne pas

- Vérifier son affectation dans l'éditeur de raccourcis.
- Vérifier qu'une autre application n'utilise pas déjà la combinaison.
- Un message d'état signale lorsqu'un raccourci clavier ne peut pas être enregistré parce qu'il est indisponible ou déjà utilisé.
- Pour un contrôleur, vérifier qu'il est reconnu par l'interface joystick Windows et refaire la capture du bouton.

### L'overlay apparaît sur un mauvais écran ou avec une mauvaise orientation

Ouvrir **Display**, actualiser la liste des écrans, sélectionner l'écran voulu dans le schéma, régler la position et l'orientation, puis cliquer sur **Save**.

### L'application reste active après fermeture

Si **Minimize to Tray on Close** est activé, la fermeture masque la fenêtre. Utiliser **Exit** depuis le menu contextuel de l'icône de notification pour quitter.

---

## Crédits et soutien

Nutho313 PinAudio est un projet de **nutho313**.

- Code source et versions : [GitHub – nutho313/PinAudio](https://github.com/nutho313/PinAudio)
- Soutenir le projet : [Ko-fi – nutho313](https://ko-fi.com/nutho313)

Si l'application vous est utile dans votre pincab, vous pouvez soutenir son développement via Ko-fi.
