import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomIntBetween, randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

export const options = {
  vus: 5,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.15'],
    http_req_duration: ['p(95)<4000'],
  },
};

const BASE_URL = 'http://localhost';

const PRODUCTS = [
  { url: '/build-your-own-computer', id: 1, name: 'Build Your Own Computer' },
  { url: '/simple-product', id: 2, name: 'Simple Product' },
  { url: '/digital-download', id: 3, name: 'Digital Download' },
  { url: '/gift-card', id: 4, name: 'Gift Card' },
];

const ajaxHeaders = {
  'Accept': '*/*',
  'Accept-Language': 'pt-PT,pt;q=0.8,en;q=0.5,en-US;q=0.3',
  'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
  'Origin': 'http://localhost',
  'X-Requested-With': 'XMLHttpRequest',
  'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64; rv:137.0) Gecko/20100101 Firefox/137.0',
};

const formHeaders = {
  'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8',
  'Accept-Language': 'pt-PT,pt;q=0.8,en;q=0.5,en-US;q=0.3',
  'Content-Type': 'application/x-www-form-urlencoded',
  'Origin': 'http://localhost',
  'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64; rv:137.0) Gecko/20100101 Firefox/137.0',
};

// Tipos de falha que podem ser simuladas
const FAILURE_TYPES = [
  'invalid_token',
  'invalid_product',
  'invalid_quantity',
  'invalid_payment',
  'missing_billing_fields',
  'invalid_shipping',
  'invalid_attributes'
];

// Função para decidir se deve simular um erro e qual tipo
function getFailureType(probability = 0.2) {
  if (Math.random() < probability) {
    const type = FAILURE_TYPES[Math.floor(Math.random() * FAILURE_TYPES.length)];
    console.log(`💥 SIMULATING FAILURE TYPE: ${type}`);
    return type;
  }
  return null;
}

function extractToken(html) {
  const match = html.match(/<input.*?name="__RequestVerificationToken".*?value="([^"]+)"/);
  return match ? match[1] : '';
}

function toFormBody(data) {
  return Object.entries(data)
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
    .join('&');
}

function mergeCookies(oldCookies, newCookies) {
  return { ...oldCookies, ...newCookies };
}

function registerUser(cookies, vuId) {
  let res = http.get(BASE_URL + '/register', {
    headers: formHeaders,
    cookies: cookies,
  });

  check(res, { 'register page 200': (r) => r.status === 200 });

  let token = extractToken(res.body);
  let email = `user${vuId}_${Date.now()}@test.com`;

  let registerData = {
    'FirstName': 'Test',
    'LastName': 'User',
    'Email': email,
    'Password': 'Test123!',
    'ConfirmPassword': 'Test123!',
    'Gender': 'Male',
    'DateOfBirthDay': '1',
    'DateOfBirthMonth': '1',
    'DateOfBirthYear': '1990',
    'Company': 'Test Company',
    'Newsletter': 'true',
    'AcceptPrivacyPolicyEnabled': 'true',
    '__RequestVerificationToken': token,
  };

  let res2 = http.post(BASE_URL + '/register', toFormBody(registerData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: true,
  });

  let registered = res2.status === 200 && (res2.url === BASE_URL + '/' || res2.url.includes('registerresult'));

  if (registered) {
    console.log(`✅ Usuário registrado: ${email}`);
  } else {
    console.log(`❌ Falha no registo: ${email}`);
  }

  return {
    success: registered,
    email: email,
    cookies: mergeCookies(cookies, res2.cookies || {}),
  };
}

export default function () {
  let cookies = {};
  let token = '';
  const product = PRODUCTS[Math.floor(Math.random() * PRODUCTS.length)];
  const failureType = getFailureType(0.3); // 30% de chance de causar um erro real

  console.log(`📦 Produto selecionado: ${product.name}`);

  // Homepage
  let res = http.get(BASE_URL + '/', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'homepage status 200': (r) => r.status === 200 });
  cookies = mergeCookies(cookies, res.cookies || {});

  // Registar
  let registration = registerUser(cookies, __VU);
  check(registration, { 'registration successful': () => registration.success });
  if (!registration.success) return;
  cookies = mergeCookies(cookies, registration.cookies || {});

  // Página do produto
  res = http.get(BASE_URL + product.url, {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'product page status 200': (r) => r.status === 200 });
  token = extractToken(res.body);

  if (!token) {
    console.log(`❌ Token não encontrado na página do produto`);
    return;
  }

  // --- SIMULAÇÃO DE ERRO: Produto inválido ---
  let productIdToAdd = product.id;
  if (failureType === 'invalid_product') {
    productIdToAdd = 999999; // Produto inexistente
    console.log(`🚨 Usando ID de produto inválido: ${productIdToAdd}`);
  }

  let addToCartPayload = {
    'product_attribute_1': '2',
    'product_attribute_2': '3',
    'product_attribute_3': '6',
    'product_attribute_4': '8',
    'product_attribute_5': '10',
    'addtocart_1.EnteredQuantity': '1',
    '__RequestVerificationToken': token,
  };

  // --- SIMULAÇÃO DE ERRO: Token inválido ---
  if (failureType === 'invalid_token') {
    addToCartPayload.__RequestVerificationToken = 'invalid_token_' + randomString(10);
    console.log(`🚨 Token inválido enviado`);
  }

  // --- SIMULAÇÃO DE ERRO: Quantidade inválida ---
  if (failureType === 'invalid_quantity') {
    addToCartPayload.addtocart_1.EnteredQuantity = '0';
    console.log(`🚨 Quantidade inválida (0) enviada`);
  }

  // --- SIMULAÇÃO DE ERRO: Atributos inválidos (enviar valores fora do range) ---
  if (failureType === 'invalid_attributes') {
    addToCartPayload.product_attribute_1 = '999'; // Atributo inválido
    console.log(`🚨 Atributo de produto inválido enviado`);
  }

  res = http.post(BASE_URL + `/addproducttocart/details/${productIdToAdd}/1`, toFormBody(addToCartPayload), {
    headers: ajaxHeaders,
    cookies: cookies,
  });

  console.log(`🛒 Add to cart status: ${res.status}`);
  if (res.status !== 200) {
    console.log(`⚠️ Add to cart falhou (esperado se erro foi simulado)`);
  } else {
    try {
      let body = JSON.parse(res.body);
      if (body.success === true) {
        console.log(`✅ Produto adicionado ao carrinho`);
      } else {
        console.log(`⚠️ Add to cart recusado pelo servidor: ${body.message}`);
      }
    } catch (e) {
      console.log(`❌ Resposta inválida no add to cart`);
    }
  }

  cookies = mergeCookies(cookies, res.cookies || {});

  // Carrinho
  res = http.get(BASE_URL + '/cart', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'cart page status 200': (r) => r.status === 200 });
  cookies = mergeCookies(cookies, res.cookies || {});

  // Checkout attributes (pode causar erro se token estiver inválido ou faltar)
  let checkoutAttributesData = {
    'checkout_attribute_1': '1',
    'itemquantity152': '1',
    'CountryId': '237',
    'StateProvinceId': '1828',
    'ZipPostalCode': '10021',
    'discountcouponcode': '',
    'giftcardcouponcode': '',
    '__RequestVerificationToken': token,
  };

  if (failureType === 'invalid_token') {
    checkoutAttributesData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  res = http.post(
    BASE_URL + '/shoppingcart/checkoutattributechange/True?isEditable=True',
    toFormBody(checkoutAttributesData),
    {
      headers: formHeaders,
      cookies: cookies,
      followRedirects: false,
    }
  );

  console.log(`📊 Status checkout attributes: ${res.status}`);

  cookies = mergeCookies(cookies, res.cookies || {});

  // Página de checkout
  res = http.get(BASE_URL + '/onepagecheckout', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'checkout page status 200': (r) => r.status === 200 });
  token = extractToken(res.body);
  if (!token) return;

  // Billing address
  let billingData = {
    'ShipToSameAddress': ['true', 'false'],
    'billing_address_id': '0',
    'BillingNewAddress.Id': '0',
    'BillingNewAddress.FirstName': 'John',
    'BillingNewAddress.LastName': 'Smith',
    'BillingNewAddress.Email': registration.email,
    'BillingNewAddress.Company': 'a',
    'BillingNewAddress.CountryId': '237',
    'BillingNewAddress.StateProvinceId': '1799',
    'BillingNewAddress.City': 'a',
    'BillingNewAddress.Address1': 'a',
    'BillingNewAddress.Address2': 'a',
    'BillingNewAddress.ZipPostalCode': 'a',
    'BillingNewAddress.PhoneNumber': 'a',
    'BillingNewAddress.FaxNumber': 'a',
    '__RequestVerificationToken': token,
  };

  // --- SIMULAÇÃO DE ERRO: Campos obrigatórios em falta ---
  if (failureType === 'missing_billing_fields') {
    delete billingData.BillingNewAddress.FirstName;
    delete billingData.BillingNewAddress.Email;
    delete billingData.BillingNewAddress.CountryId;
    console.log(`🚨 Campos obrigatórios do billing removidos`);
  }

  // --- SIMULAÇÃO DE ERRO: Token inválido no billing ---
  if (failureType === 'invalid_token') {
    billingData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  let billingString =
    'ShipToSameAddress=true&ShipToSameAddress=false&' +
    Object.entries(billingData)
      .filter(([key]) => key !== 'ShipToSameAddress')
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
      .join('&');

  res = http.post(BASE_URL + '/checkout/OpcSaveBilling', billingString, {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`✅ Billing status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  // Shipping method
  let shippingData = {
    'shippingoption': 'Ground___Shipping.FixedRate',
    '__RequestVerificationToken': token,
  };

  // --- SIMULAÇÃO DE ERRO: Shipping option inválida ---
  if (failureType === 'invalid_shipping') {
    shippingData.shippingoption = 'InvalidShippingMethod___InvalidProvider';
    console.log(`🚨 Método de envio inválido enviado`);
  }

  if (failureType === 'invalid_token') {
    shippingData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  res = http.post(BASE_URL + '/checkout/OpcSaveShippingMethod', toFormBody(shippingData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`✅ Shipping status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  // Payment method
  let paymentMethodData = {
    'paymentmethod': 'Payments.CheckMoneyOrder',
    '__RequestVerificationToken': token,
  };

  // --- SIMULAÇÃO DE ERRO: Método de pagamento inválido ---
  if (failureType === 'invalid_payment') {
    paymentMethodData.paymentmethod = 'InvalidPaymentMethod';
    console.log(`🚨 Método de pagamento inválido enviado`);
  }

  if (failureType === 'invalid_token') {
    paymentMethodData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  res = http.post(BASE_URL + '/checkout/OpcSavePaymentMethod', toFormBody(paymentMethodData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`📊 Payment method status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  // Payment info
  let paymentInfoData = {
    'checkout_attribute_1': '1',
    '__RequestVerificationToken': token,
  };

  if (failureType === 'invalid_token') {
    paymentInfoData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  res = http.post(BASE_URL + '/checkout/OpcSavePaymentInfo', toFormBody(paymentInfoData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`📊 Payment info status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  // Confirmação
  let confirmData = {
    '__RequestVerificationToken': token,
  };

  if (failureType === 'invalid_token') {
    confirmData.__RequestVerificationToken = 'invalid_token_' + randomString(10);
  }

  res = http.post(BASE_URL + '/checkout/OpcConfirmOrder', toFormBody(confirmData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`📊 Confirm status: ${res.status}`);

  let confirmSuccess = false;
  if (res.status === 200) {
    try {
      let body = JSON.parse(res.body);
      confirmSuccess = body.success === true || body.success === 1;
      if (confirmSuccess) {
        console.log(`✅ Pedido confirmado com sucesso`);
      } else {
        console.log(`❌ Pedido não confirmado (possível erro de validação)`);
      }
    } catch (e) {
      console.log(`❌ Resposta da confirmação não é JSON`);
    }
  }

  check(confirmSuccess, { 'order confirmation success': () => confirmSuccess });

  sleep(randomIntBetween(2, 4));
}