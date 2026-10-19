using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MiniCodex.Models;
using MiniCodex.ViewModels;

namespace MiniCodex.Views;

public partial class AccountPane
{
    private CancellationTokenSource? _predictionCancellation;
    private Guid? _predictionChatId;

    private void InitializePredictions()
    {
        _model.PropertyChanged += PredictionContextChanged;
        Unloaded += PredictionsUnloaded;
        UpdatePredictButton();
    }

    private void DisposePredictions()
    {
        ClosePredictions();
        _model.PropertyChanged -= PredictionContextChanged;
        Unloaded -= PredictionsUnloaded;
    }

    private void PredictionsUnloaded(object sender, RoutedEventArgs e) => ClosePredictions();

    private void PredictionContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.SelectedChat) or nameof(AccountViewModel.SelectedProject) or nameof(AccountViewModel.CanPredict) or "" or null)
        {
            if (_predictionChatId != _model.SelectedChat?.Id || !_model.CanCompose) ClosePredictions();
            UpdatePredictButton();
        }
    }

    private void UpdatePredictButton()
    {
        if (_closing || _disposed) return;
        var generating = _predictionCancellation is not null;
        PredictButton.IsEnabled = generating || _model.CanPredict;
        PredictIcon.Visibility = generating ? Visibility.Collapsed : Visibility.Visible;
        PredictSpinner.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        PredictButton.ToolTip = generating ? "Cancel prediction"
            : _model.CanPredict ? "Predict 1–4 next messages using the selected model. Uses account usage. Select a suggestion to append it to your draft."
            : "Open a chat with messages and sign in to generate predictions.";
        System.Windows.Automation.AutomationProperties.SetName(PredictButton, generating ? "Cancel prediction" : "Predict next message");
    }

    private async void PredictNextMessage(object sender, RoutedEventArgs e)
    {
        if (_predictionCancellation is not null || PredictionPopup.IsOpen) { ClosePredictions(); return; }
        if (_closing || !_model.CanPredict || _model.SelectedChat is not { } chat || _model.Conversation is not { } state || _model.SelectedModel is not { } model) return;
        CloseMentions();
        var cancellation = new CancellationTokenSource();
        _predictionCancellation = cancellation;
        _predictionChatId = chat.Id;
        PredictionItems.ItemsSource = null;
        PredictionStatus.Text = "Generating predictions…";
        PredictionStatus.Visibility = Visibility.Visible;
        PredictionPopup.IsOpen = true;
        UpdatePredictButton();
        try
        {
            var draft = MessagePayload.Compose(_model.Draft, _model.DraftAnnotations.Select(annotation => new ChatAnnotation(annotation.Quote, annotation.Comment)).ToArray(), _model.DraftAttachments.ToArray());
            var suggestions = await Runtime.PredictAsync(state, model, draft, _model.FastMode, cancellation.Token);
            if (cancellation.IsCancellationRequested || _closing || !PredictionPopup.IsOpen || _model.SelectedChat?.Id != chat.Id) return;
            PredictionItems.ItemsSource = suggestions;
            PredictionStatus.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { ClosePredictions(); }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !_closing && PredictionPopup.IsOpen && _model.SelectedChat?.Id == chat.Id)
            {
                PredictionStatus.Text = error.Message;
                PredictionStatus.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_predictionCancellation, cancellation)) _predictionCancellation = null;
            cancellation.Dispose();
            UpdatePredictButton();
        }
    }

    private void UsePrediction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string suggestion } || _predictionChatId != _model.SelectedChat?.Id || !_model.CanCompose) return;
        var draft = Composer.Text;
        var separator = draft.Length == 0 || draft.EndsWith('\n') ? "" : "\n";
        ClosePredictions();
        _insertingReference = true;
        Composer.BeginChange();
        try
        {
            Composer.Select(Composer.Text.Length, 0);
            Composer.Selection.Text = separator + suggestion;
        }
        finally { Composer.EndChange(); _insertingReference = false; }
        Composer.Focus();
        Composer.Select(Composer.Text.Length, 0);
    }

    private void DismissPredictions(object sender, RoutedEventArgs e) => ClosePredictions();

    private void ClosePredictions()
    {
        _predictionChatId = null;
        _predictionCancellation?.Cancel();
        PredictionPopup.IsOpen = false;
        PredictionItems.ItemsSource = null;
    }

    private void PredictionPopupClosed(object? sender, EventArgs e) => ClosePredictions();
}
