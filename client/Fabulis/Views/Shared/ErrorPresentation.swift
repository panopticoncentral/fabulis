import SwiftUI

/// Full-screen state shown when a view's initial data load fails. Replaces the
/// hand-rolled "Couldn't load X" VStacks that were copy-pasted across the
/// library views, so the retry affordance and layout stay consistent.
struct LoadFailedView: View {
    let title: String
    let message: String
    let retry: () -> Void

    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: "exclamationmark.triangle")
        } description: {
            Text(message)
        } actions: {
            Button("Retry", action: retry)
        }
    }
}

extension View {
    /// Presents a transient alert for an *action* failure (add/delete/save/etc.),
    /// as opposed to a load failure. Action errors must not reuse the full-screen
    /// load-error state: once a view's primary data is on screen, replacing it
    /// with an error page to report a failed row delete loses the user's context.
    func actionErrorAlert(_ error: Binding<String?>) -> some View {
        alert(
            "Something went wrong",
            isPresented: Binding(
                get: { error.wrappedValue != nil },
                set: { if !$0 { error.wrappedValue = nil } }),
            actions: { Button("OK", role: .cancel) {} },
            message: { Text(error.wrappedValue ?? "") })
    }
}
