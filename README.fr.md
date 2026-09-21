# 🖼️ Wallpaper Control

🌐 **Langue :** [English](README.md) · [Deutsch](README.de.md) ·
[Français](README.fr.md) · [Español](README.es.md) ·
[日本語](README.ja.md)

**Wallpaper Control** est un utilitaire Windows léger permettant de
gérer et d'afficher des diaporamas de fonds d'écran avec une
planification précise, des transitions animées, des statistiques, des
widgets de bureau natifs et diverses fonctions pratiques.

Il enrichit la gestion standard des fonds d'écran de Windows grâce à son
propre moteur de diaporama synchronisé sur l'horloge, à des effets de
transition rendus directement sur le bureau et à des widgets de bureau
optionnels. Il s'intègre proprement au bureau Windows et restaure la
gestion native des fonds d'écran lorsque l'application se ferme.

**Version actuelle : v2.0.0**

## ✨ Fonctionnalités

-   🖼️ **Contrôle du diaporama de fonds d'écran**
    -   Sélection du dossier de fonds d'écran
    -   Modification de l'intervalle du diaporama
    -   Activation ou désactivation de l'ordre aléatoire
    -   Passage immédiat au fond d'écran suivant
    -   Action **Fond d'écran suivant** mise en évidence pour un accès
        plus rapide
    -   Section dédiée au fond d'écran actuel avec actions rapides et
        info-bulle affichant le chemin complet
    -   Mise en pause et reprise du diaporama
    -   Pause automatique lorsqu'une application est en plein écran, y
        compris sur des moniteurs supplémentaires
    -   Les pauses manuelles du diaporama restent conservées
        indépendamment
    -   Épinglage du fond d'écran actuel
-   🎬 **Effets de transition des fonds d'écran**
    -   Transitions fluides rendues directement sur le bureau Windows
    -   Wipe avec direction sélectionnable : gauche, droite, haut, bas
        ou aléatoire
    -   Slide avec direction sélectionnable : gauche, droite, haut, bas
        ou aléatoire
    -   Fade
    -   Zoom avec variantes avant et arrière
    -   Split
    -   Curtain
    -   Le mode aléatoire choisit un effet différent à chaque changement
        de fond d'écran et varie également la direction ou le mode de
        zoom lorsque cela s'applique
    -   Durée de transition configurable
    -   Les icônes et outils du bureau restent visibles au-dessus de la
        couche de transition
-   🕐 **Widgets de bureau natifs**
    -   Widget Horloge avec 5 thèmes sélectionnables
    -   Widget de surveillance du système
    -   Widget Météo avec prévisions optionnelles sur 3 jours
    -   Widget Calendrier avec prise en charge d'iCalendar / ICS
    -   Widget compact d'informations sur le fond d'écran avec
        statistiques du fond actuel
    -   Widget Notes et rappels avec gestion locale des tâches et
        rappels
    -   Widget Web redimensionnable et repliable basé sur Microsoft
        WebView2
    -   Bouton optionnel Fond d'écran suivant
    -   Positionnement indépendant des widgets n'importe où sur le
        bureau
    -   Verrouillage indépendant de la position de chaque widget
    -   Mémorisation des positions et paramètres des widgets
    -   Aperçu en direct des widgets pendant la modification des
        paramètres
    -   Les widgets restent intégrés au bureau et ne demeurent pas
        au-dessus des fenêtres d'applications normales
-   🖥️ **Intégration Windows**
    -   Utilise les API natives de fonds d'écran de Windows tout en
        fournissant son propre moteur de planification et de transitions
    -   Planification du diaporama synchronisée sur l'horloge pour des
        changements précis
    -   Les changements manuels de fond d'écran ne réinitialisent pas la
        planification automatique
    -   Prise en charge de différents modes d'affichage
    -   Détection des changements de fond d'écran externes
    -   Ouverture des dossiers avec le gestionnaire de fichiers par
        défaut configuré
    -   Démarrage automatique optionnel avec Windows
    -   Restauration de la gestion native des fonds d'écran de Windows à
        la fermeture
    -   Une seule instance par utilisateur Windows
    -   Un nouveau lancement de Wallpaper Control ramène la fenêtre
        existante au premier plan
    -   Changement externe de fond d'écran via l'argument de ligne de
        commande `--next`
    -   Les commandes `--next` sont transmises de manière sécurisée à
        l'instance en cours de l'utilisateur Windows actuel
    -   Expose l'état d'activation du widget Horloge natif aux
        applications et scripts externes
-   📊 **Tableau de bord des statistiques**
    -   Statistiques persistantes des fonds d'écran, même après le
        redémarrage de l'application
    -   Suivi du nombre d'affichages et de la dernière date d'affichage
        de chaque fond d'écran
    -   Statistiques par période : aujourd'hui, hier, 7 derniers jours
        et 30 derniers jours
    -   Vues Top 10, Top 25 et statistiques complètes
    -   Indicateurs pour les fonds d'écran les plus affichés, les moins
        affichés et la moyenne des affichages
    -   Régularité de la distribution metric
    -   Graphique Top 10 des fonds d'écran
    -   Temps moyen de réapparition des fonds d'écran
    -   Analyse des fonds d'écran rarement affichés
    -   Détection des fonds d'écran qui n'ont jamais été affichés
    -   Recherche et colonnes triables
    -   Miniatures des fonds d'écran et aperçu au survol
    -   Définition d'un fond d'écran directement depuis la fenêtre des
        statistiques
    -   Ouverture des fonds d'écran ou de leurs dossiers depuis le menu
        contextuel
    -   Suppression d'entrées individuelles ou réinitialisation de
        toutes les statistiques
    -   Sauvegarde et récupération automatiques si le fichier principal
        des statistiques ne peut pas être chargé
    -   Conservation des fichiers de statistiques endommagés en vue
        d'une éventuelle récupération
-   🗑️ **Écartement rapide des fonds d'écran**
    -   Déplacement des fonds d'écran indésirables vers un dossier
        `Aussortiert` en un clic
    -   L'écartement est temporairement désactivé pendant une transition
    -   Le fond d'écran suivant est entièrement affiché avant le
        déplacement du fond écarté
    -   Dossier global d'écartement optionnel
    -   Sous-dossiers optionnels pour les différentes collections de
        fonds d'écran
    -   Annulation du dernier écartement
-   📜 **Historique des fonds d'écran**
    -   Suivi des fonds d'écran récemment affichés pendant la session
        actuelle
    -   Ouverture directe des fonds d'écran dans la visionneuse d'images
        par défaut
    -   Aperçu au survol pour une identification rapide
-   ⌨️ **Raccourcis clavier globaux**
    -   Fond d'écran suivant
    -   Pause / Reprise
    -   Afficher le fond d'écran actuel dans le gestionnaire de fichiers
    -   Écarter le fond d'écran actuel
    -   Les raccourcis peuvent être personnalisés ou désactivés
    -   Détection des raccourcis attribués en double
    -   Avertissement lorsque Windows ne peut pas enregistrer un
        raccourci sélectionné
    -   Les raccourcis peuvent être échangés entre les actions sans
        conflit avec les attributions précédentes
    -   Les raccourcis inchangés restent enregistrés lorsque d'autres
        sont modifiés
    -   Raccourci d'écartement par défaut : `Ctrl+Alt+Shift+R`
-   🔔 **Prise en charge de la zone de notification**
    -   Wallpaper Control peut continuer à fonctionner dans la zone de
        notification
    -   Double-cliquez sur l'icône de la zone de notification pour
        restaurer la fenêtre
    -   Comportement optionnel **Fermer dans la zone de notification**
        lors d'un clic sur le bouton X de la fenêtre
    -   Fermeture directe de l'application depuis le menu de la zone de
        notification
-   🎨 **Interface et apparence**
    -   Navigation principale permanente à gauche avec **Fond d'écran**
        et **Widgets** comme sections de même niveau, plus Paramètres et
        À propos
    -   Espace Widgets dédié ; la configuration des widgets est séparée
        des paramètres généraux
    -   Navigation des widgets triée alphabétiquement selon leur nom
        localisé
    -   Aperçu commun avec **Enregistrer**, **Ignorer** et **Restaurer
        les valeurs par défaut**
    -   Confirmation pour les modifications non enregistrées ; Ignorer
        restaure aussi les positions et dimensions prévisualisées
    -   Espace Fond d'écran repensé avec **Diaporama** et **Affichage**
        côte à côte
    -   Fenêtre principale fixe de 1260 × 800 pixels logiques ;
        réduction autorisée, redimensionnement manuel et agrandissement
        désactivés
    -   Interface des paramètres repensée
    -   Application principale repensée pour correspondre à l'interface
        des paramètres
    -   Apparence moderne et cohérente dans toute l'application
    -   Sélection des thèmes Système, Sombre et Clair
    -   Le thème Système suit automatiquement le thème des applications
        Windows
    -   Opacité de la fenêtre réglable
    -   Mémorisation de la position de la fenêtre
    -   Prise en charge du glisser-déposer
    -   Interface des paramètres réorganisée
    -   Les paramètres des widgets sont triés selon leurs noms localisés
        et commencent repliés
    -   Les paramètres s'ouvrent toujours sur l'onglet **Général**
    -   Fenêtre principale actualisée avec des groupes plus clairs et
        une meilleure hiérarchie visuelle
    -   Listes déroulantes sombres et meilleure lisibilité des contrôles
        désactivés
    -   Ordre de tabulation au clavier amélioré et espacement cohérent
    -   Réinitialisation séparée de l'apparence
    -   Interface localisée

## 🎮 Pause en plein écran

Wallpaper Control peut réduire automatiquement l'activité en
arrière-plan lorsqu'une application est en plein écran.

-   Détecte les applications en plein écran sur tous les moniteurs
    connectés
-   Met en pause les changements automatiques de fond d'écran et les
    animations de transition
-   Suspend les actualisations régulières des widgets de bureau
-   Reporte les vérifications automatiques de mises à jour
-   Utilise une détection de sortie du plein écran plus rapide et
    stabilisée et replace les widgets correctement dans l'ordre du
    bureau après la reprise
-   Un bref Alt-Tab ne relance donc pas immédiatement l'activité
    suspendue
-   Un diaporama mis en pause manuellement reste en pause à la fin du
    mode plein écran
-   La détection du plein écran est activée par défaut et peut être
    désactivée dans les paramètres

## 🔄 Vérification des mises à jour

Wallpaper Control peut rechercher de nouvelles versions dans GitHub
Releases tout en laissant à l'utilisateur le contrôle des mises à jour.

-   Vérification manuelle disponible dans les paramètres
-   Vérification automatique optionnelle à chaque démarrage de Wallpaper
    Control
-   Tant que l'application reste ouverte, les vérifications automatiques
    se répètent toutes les 24 heures
-   Des boîtes de dialogue dédiées affichent la version installée et la
    dernière version disponible
-   La page de la version peut être ouverte directement lorsqu'une
    nouvelle version est disponible
-   Les vérifications automatiques peuvent être désactivées dans les
    paramètres
-   Wallpaper Control **ne télécharge ni n'installe jamais les mises à
    jour automatiquement**

Si la version installée est déjà à jour, les vérifications automatiques
restent silencieuses. Les vérifications manuelles donnent toujours un
retour.

## 🕐 Widgets de bureau

Wallpaper Control comprend des widgets natifs qui s'intègrent
directement au bureau Windows.

Les widgets peuvent être positionnés indépendamment et verrouillés.
Leurs positions et paramètres sont conservés entre les sessions.

Les modifications des widgets sont prévisualisées immédiatement dans
l'espace Widgets dédié pendant leur configuration. Elles sont appliquées
définitivement lors de l'enregistrement des paramètres. L'annulation
restaure l'état et la position précédents.

### Horloge

L'horloge de bureau propose :

-   Affichage des heures et minutes
-   Secondes optionnelles
-   Format de date localisé
-   Taille réglable
-   5 thèmes visuels sélectionnables
-   Positionnement libre
-   Verrouillage optionnel de la position

L'horloge utilise automatiquement la langue sélectionnée dans Wallpaper
Control.

### 🖥️ Moniteur système

Le widget Système offre directement sur le bureau un aperçu des
principales informations matérielles et système.

Il peut afficher :

-   Utilisation du CPU
-   Température du CPU
-   Utilisation de la RAM
-   Utilisation du GPU
-   Température du GPU
-   Utilisation de la VRAM
-   Activité réseau en téléchargement
-   Activité réseau en envoi
-   Utilisation des lecteurs

Le widget comprend des barres graphiques compactes et propose les styles
**Minimal**, **Clean** et **Glow**.

La surveillance matérielle fonctionne de manière asynchrone afin que les
mises à jour des capteurs n'affectent ni les transitions ni la
réactivité de l'application principale.

### 🌦️ Météo

Le widget Météo affiche les informations météorologiques actuelles
directement sur le bureau.

Il peut afficher :

-   Température actuelle
-   Température ressentie
-   Conditions météorologiques actuelles
-   Humidité
-   Précipitations
-   Vitesse du vent
-   Prévisions optionnelles sur 3 jours

Les données météorologiques sont fournies par **Open-Meteo** et ne
nécessitent aucune clé API.

Le lieu peut être configuré dans les paramètres et les informations
météo peuvent être actualisées automatiquement à des intervalles
sélectionnables.

Le widget Météo propose les styles **Minimal**, **Clean** et **Glow** et
peut être positionné et verrouillé indépendamment.

### 📅 Calendrier

Le widget Calendrier offre un aperçu compact des rendez-vous à venir
directement sur le bureau.

Les données sont chargées à partir de flux **iCalendar / ICS** en
lecture seule.

Fonctionnalités :

-   Gestion individuelle des sources de calendrier avec nom, couleur,
    type et état d'activation propres
-   Prise en charge de plusieurs sources de calendrier ICS
-   Migration automatique des sources existantes vers le nouveau format
-   Les événements utilisent la couleur attribuée à leur source de
    calendrier
-   Rendez-vous avec horaire
-   Événements sur toute la journée
-   Événements récurrents
-   Événements sur plusieurs jours
-   Les événements sur plusieurs jours sont affichés pour chaque journée
    concernée
-   Plusieurs rendez-vous le même jour sont regroupés
-   Affiche les prochains jours contenant réellement des rendez-vous
-   Les jours vides sont ignorés
-   Affichage optionnel du lieu de l'événement
-   Dimensionnement automatique du widget selon les rendez-vous affichés
-   Hauteur maximale du widget configurable
-   Contenu du calendrier défilable lorsque la hauteur maximale
    configurée est atteinte
-   Défilement à la molette, barre de défilement déplaçable et
    défilement par page
-   En-tête et pied de page fixes pendant le défilement
-   Intervalle d'actualisation configurable
-   Styles **Minimal**, **Clean** et **Glow**
-   Positionnement et verrouillage indépendants

Les adresses ICS privées sont stockées sous forme chiffrée à l'aide de
la **Windows Data Protection API (DPAPI)** pour l'utilisateur Windows
actuel.

Les URL de calendriers privés sont masquées par défaut dans les
paramètres et peuvent être affichées explicitement pour être consultées
ou modifiées.

Wallpaper Control lit uniquement les flux de calendrier et ne modifie
aucune donnée.

#### 🎌 Calendriers de jours fériés

Des sources ICS distinctes peuvent être configurées comme calendriers de
jours fériés.

Les jours fériés sont mis en évidence visuellement et placés
automatiquement avant les rendez-vous normaux du même jour, afin de
faciliter leur identification.

Plusieurs sources de calendriers normaux et de jours fériés peuvent être
combinées dans le même widget Calendrier.

Les actualisations du calendrier résistent aux défaillances temporaires
des flux. Les événements déjà chargés restent visibles lorsqu'une source
devient indisponible, tandis que les flux disponibles continuent d'être
actualisés. Wallpaper Control indique lorsque les données mises en cache
peuvent être obsolètes et supprime l'avertissement dès que toutes les
sources configurées ont été actualisées avec succès. Les événements en
cache sont conservés pendant la session actuelle.

### 🌐 Web

Le widget Web intègre directement sur le bureau un site configuré grâce
à **Microsoft WebView2**.

-   Adresse HTTP/HTTPS et nom d'affichage configurés uniquement depuis
    l'espace Widgets
-   Zoom réglable et interaction avec le site optionnelle
-   Profil de navigateur persistant pour les cookies et sessions
-   Redimensionnement libre depuis tous les bords et coins et
    déplacement via l'en-tête personnalisé
-   Repli en barre de titre compacte et restauration de la taille
    précédente sans rechargement inutile
-   Rechargement manuel de la page affichée
-   Mémorisation de la position, de la taille déployée et de l'état
    replié
-   **Verrouiller position/taille** et **Autoriser l'interaction avec le
    site** sont indépendants
-   États de chargement/erreur localisés ; fenêtres contextuelles et
    téléchargements bloqués

Wallpaper Control ne lit ni ne stocke **aucun mot de passe de site
Web**. Les identifiants et sessions sont gérés par le profil WebView2.
Le widget Web reste intégré à la gestion existante d'Afficher le bureau
et du plein écran.

### ℹ️ Informations sur le fond d'écran

Le widget compact Informations sur le fond d'écran peut afficher le nom
du fond actuel, son nombre d'affichages et le nombre total de fonds
disponibles. Une seconde ligne optionnelle ajoute la moyenne, le total
des affichages et le record. La taille de police est réglable de
**10--24 px**. Les extensions et plusieurs suffixes séparés par des
virgules peuvent être masqués uniquement dans le nom affiché. Les noms
longs sont automatiquement raccourcis. Le widget prend en charge
**Minimal**, **Clean** et **Glow**.

### 📝 Notes et rappels

Le widget Notes et rappels offre une gestion locale légère des tâches et
rappels sur le bureau. Les entrées peuvent contenir un titre, une
description et une date/heure optionnelles, et sont regroupées en Sans
date, En retard, Aujourd'hui, Demain et jours à venir. Elles peuvent
être créées, modifiées, terminées, supprimées et rouvertes depuis le
widget et son gestionnaire. Le contenu défile sous un en-tête fixe, la
hauteur maximale est configurable et les contrôles restent utilisables
lorsque la position est verrouillée. Le gestionnaire et l'éditeur
suivent le thème Clair/Sombre actuel. Les données sont stockées
localement en JSON avec écriture atomique, sauvegarde et protection des
fichiers endommagés. Le widget prend en charge **Minimal**, **Clean** et
**Glow**.

Les dates et heures sont affichées dans le widget. **Les notifications
Windows ne sont pas incluses dans v1.8.6.**

### Fond d'écran suivant

Le widget Fond d'écran suivant fournit un bouton compact permettant de
passer immédiatement au fond d'écran suivant.

Il propose les styles **Minimal**, **Clean** et **Glow**. Les
changements sont immédiatement visibles dans l'aperçu en direct et le
style choisi est mémorisé entre les sessions. Les configurations
existantes continuent d'utiliser l'apparence **Minimal** par défaut.

Il peut être positionné et verrouillé indépendamment des autres widgets
et utilise la même logique de changement et de transition que
l'application principale.

## 📊 Statistiques

Wallpaper Control conserve des statistiques persistantes sur les fonds
d'écran affichés par son diaporama.

Le tableau de bord des statistiques peut afficher :

-   Nombre total d'affichages pour chaque fond d'écran
-   Date du dernier affichage de chaque fond d'écran
-   Part des affichages et classement de popularité
-   Statistiques pour aujourd'hui, hier, les 7 derniers jours et les 30
    derniers jours
-   Fonds d'écran les plus et les moins affichés
-   Nombre moyen d'affichages
-   Régularité de la distribution
-   Temps moyen de réapparition
-   Graphique Top 10
-   Fonds d'écran jamais affichés ou non affichés depuis longtemps

Les statistiques sont stockées localement et conservées après le
redémarrage de l'application.

Wallpaper Control conserve le fichier de statistiques précédent comme
sauvegarde. Si le fichier principal ne peut pas être chargé,
l'application peut automatiquement utiliser la sauvegarde tout en
conservant les fichiers endommagés en vue d'une éventuelle récupération.
Les statistiques sont écrites via des fichiers temporaires uniques afin
de réduire le risque de collisions ou de remplacements incomplets.

Les statistiques temporelles et le suivi des réapparitions commencent
lors de la première initialisation des données correspondantes. Les
données historiques antérieures ne sont pas reconstruites.

L'historique récent des fonds d'écran reste lié à la session et est
effacé lorsque Wallpaper Control est complètement fermé.

## 🗑️ Écarter des fonds d'écran

Le fond d'écran actuel ne vous plaît pas ?

Wallpaper Control passe d'abord au fond d'écran suivant et attend la fin
du changement avant de déplacer l'image indésirable dans un dossier
`Aussortiert`. L'écartement est temporairement désactivé lorsqu'une
transition est déjà en cours afin d'éviter que l'image affichée ne soit
déplacée avant la fin de la transition.

La destination peut se trouver dans le dossier de fonds d'écran actuel
ou être configurée comme dossier global d'écartement.

Vous avez écarté la mauvaise image par erreur ? La dernière action peut
être annulée pendant la session actuelle.

## 🕹️ Contrôle externe

Wallpaper Control peut recevoir des commandes d'applications externes,
de scripts, de raccourcis ou d'outils de bureau pendant son
fonctionnement.

### Fond d'écran suivant

La commande suivante est prise en charge :

``` text
WallpaperControl.exe --next
```

Cette commande envoie une requête à l'instance Wallpaper Control en
cours et passe immédiatement au fond d'écran suivant.

Wallpaper Control fonctionne en une seule instance pour l'utilisateur
Windows actuel. Un nouveau lancement normal ramène la fenêtre principale
existante au premier plan. Les communications de commandes telles que
`--next` sont limitées à l'utilisateur actuel, et les requêtes mal
formées, trop volumineuses ou bloquées sont rejetées sans empêcher les
commandes suivantes.

Les requêtes externes utilisent la même gestion du diaporama, de la
planification et des transitions que les changements déclenchés
directement dans Wallpaper Control.

Cela permet d'intégrer Wallpaper Control à des scripts personnalisés,
lanceurs, outils d'automatisation ou autres applications de bureau sans
ouvrir la fenêtre principale.

### Horloge Widget State

Les applications externes peuvent déterminer si l'horloge native de
Wallpaper Control est activée en lisant :

``` text
HKEY_CURRENT_USER\Software\WallpaperControl
```

Valeur du Registre :

``` text
ClockWidgetEnabled
```

Valeurs :

``` text
0 = Widget Horloge natif désactivé
1 = Widget Horloge natif activé
```

Cela permet aux applications externes ou outils de bureau d'adapter leur
comportement selon que l'horloge native de Wallpaper Control est activée
ou non.

La valeur du Registre représente le paramètre enregistré du widget. Les
modifications d'aperçu effectuées lorsque la fenêtre des paramètres est
ouverte ne sont appliquées définitivement qu'après enregistrement.

## 🩺 Diagnostic

Wallpaper Control comprend une journalisation légère destinée aux
erreurs et défaillances inattendues.

Les journaux ne sont créés qu'en cas de besoin et sont stockés dans :

``` text
%APPDATA%\WallpaperControl\Logs
```

Le fichier journal principal est :

``` text
wallpaper-control.log
```

La journalisation est destinée au dépannage et ne nécessite aucune
configuration supplémentaire en utilisation normale.

## 🌍 Langues

Wallpaper Control comprend actuellement :

-   🇩🇪 Allemand
-   🇬🇧 Anglais
-   🇫🇷 Français
-   🇪🇸 Espagnol
-   🇯🇵 Japonais

La langue de l'interface peut être modifiée directement dans les
paramètres de l'application.

Les textes et le format des dates des widgets suivent la langue
sélectionnée.

## 💻 Configuration requise

-   **Windows 11 :** pris en charge et testé
-   **Windows 10 :** devrait être compatible, actuellement non testé
-   Windows 64 bits
-   Aucune installation séparée de .NET requise

## 🚀 Installation

La **méthode d'installation officielle** de Wallpaper Control est
l'installateur Windows x64 fourni avec chaque version.

1.  Téléchargez `WallpaperControl-2.0.0-Setup-x64.exe` depuis la
    dernière version GitHub.
2.  Fermez complètement toute instance existante de Wallpaper Control
    depuis la zone de notification avant l'installation ou la mise à
    niveau.
3.  Lancez l'installateur.
4.  Sélectionnez éventuellement un raccourci sur le Bureau pendant
    l'installation.
5.  Lancez Wallpaper Control depuis le menu Démarrer ou le raccourci du
    Bureau.

Wallpaper Control est installé pour l'utilisateur Windows actuel et **ne
nécessite pas de droits d'administrateur**.

L'installateur contient le **runtime .NET** requis, aucune installation
séparée de .NET n'est donc nécessaire. Un raccourci dans le menu
Démarrer est créé automatiquement, tandis que celui du Bureau est
optionnel.

Lors de la mise à niveau d'une installation existante, les paramètres et
statistiques sont conservés. Si le démarrage automatique était déjà
activé, son entrée est mise à jour avec le chemin de l'application
installée.

La désinstallation supprime l'application et ses raccourcis tout en
conservant les paramètres et statistiques de l'utilisateur pour une
réutilisation ultérieure.

L'installateur n'est actuellement **pas signé numériquement**. Windows
peut donc afficher un avertissement de sécurité lors de son lancement.

## 🔒 Confidentialité

Wallpaper Control stocke localement sur votre ordinateur ses paramètres
et statistiques de fonds d'écran.

Aucun compte Wallpaper Control n'est requis.

La plupart des fonctions, notamment la gestion des fonds d'écran, le
diaporama, les transitions, la détection du plein écran et les
statistiques, fonctionnent entièrement en local.

Certaines fonctions optionnelles nécessitent une connexion Internet :

-   Le **widget Météo** se connecte à Open-Meteo pour récupérer les
    informations météorologiques.
-   Le **widget Calendrier** se connecte aux adresses iCalendar / ICS
    configurées pour récupérer les données.
-   La **vérification des mises à jour** optionnelle se connecte à
    GitHub Releases pour déterminer si une nouvelle version est
    disponible. Les vérifications automatiques peuvent être désactivées
    et Wallpaper Control ne télécharge ni n'installe jamais de mise à
    jour automatiquement.

Les adresses ICS privées configurées pour le widget Calendrier sont
stockées sous forme chiffrée à l'aide de la Windows Data Protection API
(DPAPI) pour l'utilisateur Windows actuel.

L'accès au calendrier est en lecture seule. Wallpaper Control ne modifie
ni les rendez-vous ni les données du calendrier.

Les journaux de diagnostic sont stockés localement et ne sont créés
qu'en cas de besoin pour le dépannage.

## 🛠️ Développé avec

-   C#
-   .NET 10
-   Windows Forms
-   Microsoft WebView2
-   API Windows natives / intégration COM
-   Open-Meteo
-   iCalendar / ICS

## 📄 Licence

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control est un logiciel libre et open source distribué sous la
**GNU General Public License v3.0 (GPL-3.0)**.

Vous êtes libre d'utiliser, d'étudier, de modifier et de redistribuer
Wallpaper Control selon les termes de la GNU General Public License
v3.0.

Consultez le fichier `LICENSE` pour le texte complet de la licence.

------------------------------------------------------------------------

**Wallpaper Control**\
Un peu plus de contrôle sur ce que Windows affiche sur votre bureau. 🖼️
