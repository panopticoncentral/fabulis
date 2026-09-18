import SwiftUI

private struct EditorProtection: ViewModifier {
    @Environment(AppState.self) private var appState
    @State private var identity = UUID()
    let isDirty: Bool

    func body(content: Content) -> some View {
        content
            .interactiveDismissDisabled(isDirty)
            .onAppear {
                if isDirty { appState.dirtyEditors.insert(identity) }
            }
            .onChange(of: isDirty, initial: true) { _, dirty in
                if dirty { appState.dirtyEditors.insert(identity) }
                else { appState.dirtyEditors.remove(identity) }
            }
            .onDisappear { appState.dirtyEditors.remove(identity) }
    }
}

extension View {
    func protectUnsavedChanges(_ isDirty: Bool) -> some View {
        modifier(EditorProtection(isDirty: isDirty))
    }

    func discardChangesConfirmation(isPresented: Binding<Bool>, onDiscard: @escaping () -> Void) -> some View {
        confirmationDialog("Discard changes?", isPresented: isPresented, titleVisibility: .visible) {
            Button("Discard Changes", role: .destructive, action: onDiscard)
            Button("Keep Editing", role: .cancel) {}
        }
    }

    func touchTarget() -> some View {
        #if targetEnvironment(macCatalyst)
        self
        #else
        frame(minWidth: 44, minHeight: 44)
        #endif
    }
}
