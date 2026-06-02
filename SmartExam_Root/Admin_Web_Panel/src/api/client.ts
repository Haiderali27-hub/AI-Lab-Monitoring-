import axios from 'axios';
import { API_BASE_URL } from '../utils/constants';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
});

// Automatically attach JWT to every request
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('smartexam_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// If token expires, send user back to login
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('smartexam_token');
      localStorage.removeItem('smartexam_user');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export default apiClient;
