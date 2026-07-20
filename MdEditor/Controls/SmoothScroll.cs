using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit;

namespace MdEditor.Controls;

/// <summary>
/// Défilement animé pour les contrôles dont l'offset est en lecture seule.
/// <para>
/// Deux mécanismes, parce que les deux usages n'ont pas les mêmes contraintes :
/// </para>
/// <list type="bullet">
/// <item>horizontal (barre d'onglets) — déplacements ponctuels déclenchés par un clic, animés par une
/// <see cref="DoubleAnimation"/> sur une propriété attachée, <see cref="ScrollViewer.HorizontalOffset"/>
/// n'étant pas assignable ;</item>
/// <item>vertical (éditeur) — flux continu de crans de molette, suivi image par image vers une cible
/// mise à jour en vol (voir <see cref="VerticalScroller"/>).</item>
/// </list>
/// </summary>
public static class SmoothScroll
{
    /// <summary>Cible d'un défilement horizontal animé (ScrollViewer).</summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "HorizontalOffset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnHorizontalOffsetChanged));

    private static readonly DependencyProperty VerticalScrollerProperty =
        DependencyProperty.RegisterAttached(
            "VerticalScroller", typeof(VerticalScroller), typeof(SmoothScroll), new PropertyMetadata(null));

    private static void OnHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToHorizontalOffset((double)e.NewValue);
        }
    }

    public static void AnimateHorizontal(ScrollViewer scrollViewer, double target, TimeSpan duration)
    {
        var from = scrollViewer.HorizontalOffset;
        // Repartir de l'offset réel : sans ce reset l'animation démarrerait à la dernière valeur animée,
        // qui peut avoir été invalidée entre-temps (redimensionnement, onglet fermé...).
        scrollViewer.BeginAnimation(HorizontalOffsetProperty, null);
        scrollViewer.SetValue(HorizontalOffsetProperty, from);
        scrollViewer.BeginAnimation(HorizontalOffsetProperty, new DoubleAnimation(from, target, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    /// <summary>Déplace la cible du défilement vertical ; le suivi démarre s'il ne tourne pas déjà.</summary>
    public static void ScrollVerticalTo(TextEditor editor, double target) =>
        GetVerticalScroller(editor).ScrollTo(target);

    /// <summary>
    /// Destination visée par le suivi en cours, ou l'offset réel s'il est à l'arrêt. Point de départ à
    /// utiliser pour cumuler un nouveau delta de molette.
    /// </summary>
    public static double PendingVerticalTarget(TextEditor editor) =>
        GetVerticalScroller(editor).PendingTarget;

    /// <summary>
    /// Arrête le suivi vertical. À appeler avant tout ScrollToVerticalOffset programmatique
    /// (restauration d'onglet, résultat de recherche, sync depuis l'aperçu) : sinon le suivi continue
    /// de tourner et ramène l'éditeur sur son ancienne cible.
    /// </summary>
    public static void CancelVertical(TextEditor editor) => GetVerticalScroller(editor).Stop();

    private static VerticalScroller GetVerticalScroller(TextEditor editor)
    {
        if (editor.GetValue(VerticalScrollerProperty) is not VerticalScroller scroller)
        {
            scroller = new VerticalScroller(editor);
            editor.SetValue(VerticalScrollerProperty, scroller);
        }

        return scroller;
    }

    /// <summary>
    /// Rapproche l'offset de sa cible d'une fraction constante à chaque image (approche exponentielle).
    /// <para>
    /// Une <see cref="DoubleAnimation"/> par cran de molette ne convient pas ici : chaque cran devrait
    /// relancer une animation, et son point de départ ne peut être lu que dans
    /// <see cref="TextEditor.VerticalOffset"/>, qui n'est rafraîchi qu'au layout suivant. Un cran
    /// arrivant en cours d'animation repartait donc d'une position périmée et rejouait un bout du
    /// mouvement — c'est exactement ce qui rendait la molette saccadée alors que l'ascenseur, qui écrit
    /// des offsets continus, restait fluide.
    /// </para>
    /// <para>
    /// Ici l'offset courant est tenu en interne : déplacer la cible en vol ne fait qu'allonger la
    /// distance restante, sans rupture de vitesse ni relecture d'un état en retard.
    /// </para>
    /// </summary>
    private sealed class VerticalScroller
    {
        // Constante de temps de l'approche : ~95 % de la distance parcourue en 3 tau.
        private const double TauSeconds = 0.045;
        private const double SettleThreshold = 0.5;

        private readonly TextEditor _editor;
        private double _current;
        private double _target;
        private TimeSpan _lastFrame;
        private bool _running;

        public VerticalScroller(TextEditor editor) => _editor = editor;

        public double PendingTarget => _running ? _target : _editor.VerticalOffset;

        public void ScrollTo(double target)
        {
            _target = target;
            if (_running)
            {
                return;
            }

            _current = _editor.VerticalOffset;
            _lastFrame = TimeSpan.MinValue;
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            // CompositionTarget.Rendering peut être levé plusieurs fois pour la même image : sans ce
            // garde-fou, dt vaudrait 0 et l'approche n'avancerait pas.
            var dt = _lastFrame == TimeSpan.MinValue ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
            if (dt <= 0)
            {
                return;
            }

            _lastFrame = now;

            var remaining = _target - _current;
            if (Math.Abs(remaining) < SettleThreshold)
            {
                _current = _target;
                _editor.ScrollToVerticalOffset(_current);
                Stop();
                return;
            }

            _current += remaining * (1 - Math.Exp(-dt / TauSeconds));
            _editor.ScrollToVerticalOffset(_current);
        }
    }
}
