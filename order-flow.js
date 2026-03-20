import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  vus: 1,
  duration: '30s',
};

const HOST = __ENV.HOST || 'http://localhost:80';

export default function () {
  http.get(HOST + '/');
  http.get(HOST + '/product/1');
  sleep(1);
}