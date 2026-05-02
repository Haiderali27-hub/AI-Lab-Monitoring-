import { useEffect, useState } from 'react'

export type ToastType = 'success' | 'error' | 'warning' | 'info'

export type Toast = {
  id: string
  message: string
  type: ToastType
  duration?: number
}

type ToastItemProps = {
  toast: Toast
  onDismiss: (id: string) => void
}

const ICONS: Record<ToastType, string> = {
  success: 'check_circle',
  error: 'error',
  warning: 'warning',
  info: 'info',
}

function ToastItem({ toast, onDismiss }: ToastItemProps) {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    // Trigger enter animation
    const t = setTimeout(() => setVisible(true), 10)
    return () => clearTimeout(t)
  }, [])

  useEffect(() => {
    const duration = toast.duration ?? 4000
    const t = setTimeout(() => {
      setVisible(false)
      setTimeout(() => onDismiss(toast.id), 300)
    }, duration)
    return () => clearTimeout(t)
  }, [toast, onDismiss])

  return (
    <div
      className={`toast-item toast-${toast.type} ${visible ? 'toast-visible' : ''}`}
      role="alert"
      aria-live="polite"
    >
      <span className="material-symbols-outlined toast-icon">{ICONS[toast.type]}</span>
      <span className="toast-message">{toast.message}</span>
      <button
        className="toast-close"
        onClick={() => {
          setVisible(false)
          setTimeout(() => onDismiss(toast.id), 300)
        }}
        aria-label="Dismiss"
      >
        <span className="material-symbols-outlined">close</span>
      </button>
    </div>
  )
}

type ToasterProps = {
  toasts: Toast[]
  onDismiss: (id: string) => void
}

export function Toaster({ toasts, onDismiss }: ToasterProps) {
  if (toasts.length === 0) return null
  return (
    <div className="toast-container" aria-label="Notifications">
      {toasts.map((t) => (
        <ToastItem key={t.id} toast={t} onDismiss={onDismiss} />
      ))}
    </div>
  )
}

// Hook: useToast()
let _toastIdCounter = 0

export function useToast() {
  const [toasts, setToasts] = useState<Toast[]>([])

  function push(message: string, type: ToastType = 'success', duration?: number) {
    const id = `toast-${++_toastIdCounter}`
    setToasts((prev) => [...prev, { id, message, type, duration }])
  }

  function dismiss(id: string) {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }

  return { toasts, push, dismiss }
}
