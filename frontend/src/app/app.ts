import { Component, signal, OnInit, inject } from '@angular/core';
import { HttpClient, HttpEventType } from '@angular/common/http';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.css',
  standalone: true,
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);

  // API URL resolver (uses local 5008 in dev, Azure domain in prod)
  private readonly apiBaseUrl =
    window.location.hostname === 'localhost'
      ? 'http://localhost:5008'
      : 'https://ca-cvmatch-api.wonderfulcliff-cd80baae.westus.azurecontainerapps.io';

  // Signals for application state
  protected readonly isAuthenticated = signal<boolean>(false);
  protected readonly userName = signal<string>('');
  protected readonly userEmail = signal<string>('');
  protected readonly token = signal<string>('');

  // Drag & drop state signals
  protected readonly isDragActive = signal<boolean>(false);
  protected readonly isUploading = signal<boolean>(false);
  protected readonly uploadProgress = signal<number>(0);

  // Feedback alerts signals
  protected readonly errorMessage = signal<string>('');
  protected readonly successMessage = signal<string>('');

  // Data signals
  protected readonly fileId = signal<string>('');
  protected readonly extractedText = signal<string>('');

  // Optimization signals
  protected readonly jobTitle = signal<string>('');
  protected readonly jobDescription = signal<string>('');
  protected readonly isOptimizing = signal<boolean>(false);
  protected readonly atsMatchScore = signal<number | null>(null);
  protected readonly optimizedCvMarkdown = signal<string>('');
  protected readonly optimizationStep = signal<string>('');
  protected readonly isCopied = signal<boolean>(false);

  // Structuring state signals
  protected readonly isStructuring = signal<boolean>(false);
  protected readonly isStructured = signal<boolean>(false);

  ngOnInit(): void {
    this.checkAuthentication();
  }

  private checkAuthentication(): void {
    // 1. Check if token and user details are present in URL query parameters (OAuth callback redirect)
    const urlParams = new URLSearchParams(window.location.search);
    const tokenParam = urlParams.get('token');
    const emailParam = urlParams.get('email');
    const nameParam = urlParams.get('name');

    if (tokenParam && emailParam && nameParam) {
      // Save credentials to localStorage
      localStorage.setItem('cv_match_token', tokenParam);
      localStorage.setItem('cv_match_email', emailParam);
      localStorage.setItem('cv_match_name', nameParam);

      // Clean query parameters from URL
      const cleanUrl = window.location.origin + window.location.pathname;
      window.history.replaceState({}, document.title, cleanUrl);
    }

    // 2. Read from localStorage to establish local auth session state
    const savedToken = localStorage.getItem('cv_match_token');
    const savedEmail = localStorage.getItem('cv_match_email');
    const savedName = localStorage.getItem('cv_match_name');

    if (savedToken && savedEmail && savedName) {
      this.token.set(savedToken);
      this.userEmail.set(savedEmail);
      this.userName.set(savedName);
      this.isAuthenticated.set(true);
    }
  }

  protected loginWithGoogle(): void {
    // Redirect browser to backend Google login flow
    window.location.href = `${this.apiBaseUrl}/api/auth/login`;
  }

  protected logout(): void {
    // Clear storage session
    localStorage.removeItem('cv_match_token');
    localStorage.removeItem('cv_match_email');
    localStorage.removeItem('cv_match_name');

    // Reset state signals
    this.token.set('');
    this.userEmail.set('');
    this.userName.set('');
    this.isAuthenticated.set(false);
    this.extractedText.set('');
    this.fileId.set('');
    this.errorMessage.set('');
    this.successMessage.set('');
  }

  // --- Drag & Drop event handlers ---
  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragActive.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragActive.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragActive.set(false);

    if (event.dataTransfer && event.dataTransfer.files.length > 0) {
      const file = event.dataTransfer.files[0];
      this.handleFileSelection(file);
    }
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.handleFileSelection(file);
    }
  }

  private handleFileSelection(file: File): void {
    this.errorMessage.set('');
    this.successMessage.set('');

    // Validation 1: Format check (.pdf only)
    const extension = file.name.split('.').pop()?.toLowerCase();
    if (file.type !== 'application/pdf' && extension !== 'pdf') {
      this.triggerAlertError('Solo se permiten archivos en formato PDF (.pdf).');
      return;
    }

    // Validation 2: Size check (<2MB)
    const maxSizeBytes = 2 * 1024 * 1024;
    if (file.size > maxSizeBytes) {
      this.triggerAlertError('El archivo excede el límite permitido de 2MB.');
      return;
    }

    // Process upload
    this.uploadPdf(file);
  }

  private triggerAlertError(message: string): void {
    this.errorMessage.set(message);
    // Clear error message after 6 seconds
    setTimeout(() => {
      if (this.errorMessage() === message) {
        this.errorMessage.set('');
      }
    }, 6000);
  }

  private uploadPdf(file: File): void {
    this.isUploading.set(true);
    this.uploadProgress.set(0);

    const formData = new FormData();
    formData.append('file', file);

    this.http
      .post<any>(`${this.apiBaseUrl}/api/upload`, formData, {
        headers: {
          Authorization: `Bearer ${this.token()}`,
        },
        reportProgress: true,
        observe: 'events',
      })
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            // Update upload progress percentage
            this.uploadProgress.set(Math.round((100 * event.loaded) / event.total));
          } else if (event.type === HttpEventType.Response) {
            this.isUploading.set(false);
            this.successMessage.set('¡Archivo subido y procesado con éxito!');
            setTimeout(() => this.successMessage.set(''), 4000);

            const response = event.body;
            this.fileId.set(response.fileId || response.FileId || '');
            this.extractedText.set(response.extractedText || response.ExtractedText || '');
          }
        },
        error: (err) => {
          this.isUploading.set(false);
          let errorText = 'Error en el servidor al procesar el archivo.';

          if (err.error && typeof err.error === 'object') {
            errorText = err.error.message || err.error.Message || errorText;
          } else if (err.error && typeof err.error === 'string') {
            try {
              const parsed = JSON.parse(err.error);
              errorText = parsed.message || parsed.Message || errorText;
            } catch {
              errorText = err.error;
            }
          }

          this.triggerAlertError(errorText);
        },
      });
  }

  protected optimizeCv(): void {
    const desc = this.jobDescription().trim();
    const title = this.jobTitle().trim();

    if (!title) {
      this.triggerAlertError('Por favor, ingresa el título del puesto.');
      return;
    }

    if (desc.length < 100 || desc.length > 10000) {
      this.triggerAlertError(
        'La descripción de la vacante debe tener entre 100 y 10,000 caracteres.',
      );
      return;
    }

    this.isOptimizing.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const body = {
      JobTitle: title,
      JobDescription: desc,
    };

    this.http
      .post<any>(`${this.apiBaseUrl}/api/cv/optimize`, body, {
        headers: {
          Authorization: `Bearer ${this.token()}`,
        },
      })
      .subscribe({
        next: (response) => {
          this.isOptimizing.set(false);
          this.successMessage.set('¡CV optimizado con éxito!');
          setTimeout(() => this.successMessage.set(''), 4000);

          this.atsMatchScore.set(response.atsMatchScore ?? response.AtsMatchScore ?? 0);
          this.optimizedCvMarkdown.set(
            response.optimizedCvMarkdown ?? response.OptimizedCvMarkdown ?? '',
          );
        },
        error: (err) => {
          this.isOptimizing.set(false);
          let errorText = 'Error al optimizar el CV.';
          if (err.error && typeof err.error === 'object') {
            errorText = err.error.message || err.error.Message || errorText;
          } else if (err.error && typeof err.error === 'string') {
            try {
              const parsed = JSON.parse(err.error);
              errorText = parsed.message || parsed.Message || errorText;
            } catch {
              errorText = err.error;
            }
          }
          this.triggerAlertError(errorText);
        },
      });
  }

  protected copyToClipboard(): void {
    const markdown = this.optimizedCvMarkdown();
    if (!markdown) return;

    navigator.clipboard
      .writeText(markdown)
      .then(() => {
        this.successMessage.set('¡Copiado al portapapeles!');
        setTimeout(() => this.successMessage.set(''), 3000);
      })
      .catch(() => {
        this.triggerAlertError('No se pudo copiar al portapapeles.');
      });
  }

  protected resetOptimization(): void {
    this.atsMatchScore.set(null);
    this.optimizedCvMarkdown.set('');
    this.jobTitle.set('');
    this.jobDescription.set('');
  }

  protected structureProfile(): void {
    const rawText = this.extractedText();
    if (!rawText) return;

    this.isStructuring.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const rawSkills = this.extractPotentialSkills(rawText);

    this.http
      .post<any>(
        `${this.apiBaseUrl}/api/skills/match`,
        { RawSkills: rawSkills },
        {
          headers: { Authorization: `Bearer ${this.token()}` },
        },
      )
      .subscribe({
        next: (matchResponse) => {
          const canonical = matchResponse.canonicalSkills || matchResponse.CanonicalSkills || [];
          const custom = matchResponse.customSkills || matchResponse.CustomSkills || [];

          this.http
            .post<any>(
              `${this.apiBaseUrl}/api/profile/process`,
              {
                CvText: rawText,
                CanonicalSkills: canonical,
                CustomSkills: custom,
              },
              {
                headers: { Authorization: `Bearer ${this.token()}` },
              },
            )
            .subscribe({
              next: (processResponse) => {
                this.isStructuring.set(false);
                this.isStructured.set(true);
                this.successMessage.set('¡Perfil estructurado y guardado con éxito!');
                setTimeout(() => this.successMessage.set(''), 4000);
              },
              error: (err) => {
                this.isStructuring.set(false);
                this.triggerAlertError('Error al estructurar el perfil con Azure OpenAI.');
              },
            });
        },
        error: (err) => {
          this.isStructuring.set(false);
          this.triggerAlertError('Error al cruzar habilidades con la taxonomía.');
        },
      });
  }

  private extractPotentialSkills(text: string): string[] {
    if (!text) return [];
    const matches = text.match(/[A-Za-z0-9+#\.\-]+/g) || [];
    const unique = Array.from(new Set(matches));
    return unique.filter((word) => {
      const w = word.trim();
      return w.length >= 2 && w.length <= 15 && !/^\d+$/.test(w);
    });
  }
}
