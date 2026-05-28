import SwiftUI

struct OnboardingView: View {
    @Environment(AppState.self) private var appState
    @State private var serverURL: String = "http://"
    @State private var password: String = ""
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @FocusState private var focused: Field?

    enum Field { case url, password }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 32) {
                    VStack(spacing: 12) {
                        Image(systemName: "book.pages.fill")
                            .font(.system(size: 64))
                            .foregroundStyle(.tint)
                        Text("Fabulis")
                            .font(.largeTitle.bold())
                        Text("Connect to your Fabulis server")
                            .font(.title3)
                            .foregroundStyle(.secondary)
                    }
                    .padding(.top, 40)

                    VStack(alignment: .leading, spacing: 16) {
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Server URL").font(.headline)
                            TextField("http://hostname:5288", text: $serverURL)
                                .textFieldStyle(.roundedBorder)
                                .keyboardType(.URL)
                                .textContentType(.URL)
                                .autocorrectionDisabled()
                                .textInputAutocapitalization(.never)
                                .focused($focused, equals: .url)
                        }
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Vault password").font(.headline)
                            SecureField("Vault password", text: $password)
                                .textFieldStyle(.roundedBorder)
                                .textContentType(.oneTimeCode)
                                .focused($focused, equals: .password)
                        }
                        if let errorMessage {
                            Text(errorMessage).font(.caption).foregroundStyle(.red)
                        }
                    }
                    .padding(.horizontal)

                    Button {
                        Task { await submit() }
                    } label: {
                        Group {
                            if isSubmitting { ProgressView() }
                            else { Text("Connect") }
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(!canSubmit)
                    .padding(.horizontal)
                }
            }
        }
        .onAppear { focused = .url }
    }

    private var canSubmit: Bool {
        !isSubmitting
            && !serverURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && serverURL != "http://"
            && !password.isEmpty
    }

    private func submit() async {
        errorMessage = nil
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            _ = try await FabulisAPIClient.shared.unlock(
                serverURL: serverURL.trimmingCharacters(in: .whitespacesAndNewlines),
                password: password)
            appState.didCompleteOnboarding()
        } catch APIError.unauthorized {
            errorMessage = "Wrong password."
        } catch let APIError.server(status, _) {
            errorMessage = "Server returned \(status)."
        } catch let APIError.transport(err) {
            errorMessage = "Could not reach the server: \(err.localizedDescription)"
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
