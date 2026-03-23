import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

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

function shouldFail(stepName, failRate = 0.1) {
  const fail = Math.random() < failRate;
  if (fail) {
    console.log(`💥 SIMULATED FAILURE at step: ${stepName}`);
  }
  return fail;
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

  console.log(`📦 Produto selecionado: ${product.name}`);

  if (shouldFail('Homepage', 0.05)) {
    console.log(`❌ Falha simulada na homepage`);
    return;
  }

  let res = http.get(BASE_URL + '/', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'homepage status 200': (r) => r.status === 200 });
  cookies = mergeCookies(cookies, res.cookies || {});
  console.log(`🍪 Cookies após homepage: ${Object.keys(cookies).length}`);

  let registration = registerUser(cookies, __VU);
  check(registration, { 'registration successful': () => registration.success });
  if (!registration.success) return;
  cookies = mergeCookies(cookies, registration.cookies || {});

  if (shouldFail('After Registration', 0.08)) {
    console.log(`❌ Falha simulada após registo`);
    return;
  }

  res = http.get(BASE_URL + product.url, {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'product page status 200': (r) => r.status === 200 });
  token = extractToken(res.body);
  console.log(`🔑 Token da página do produto: ${token ? 'SIM' : 'NÃO'}`);

  if (!token) {
    console.log(`❌ Token não encontrado na página do produto`);
    return;
  }

  if (shouldFail('Product Page', 0.05)) {
    console.log(`❌ Falha simulada na página do produto`);
    return;
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

  res = http.post(BASE_URL + `/addproducttocart/details/${product.id}/1`, toFormBody(addToCartPayload), {
    headers: ajaxHeaders,
    cookies: cookies,
  });

  console.log(`🛒 Add to cart status: ${res.status}`);

  if (shouldFail('Add to Cart', 0.15)) {
    console.log(`❌ Falha simulada no add to cart`);
    return;
  }

  if (res.status === 200) {
    try {
      let body = JSON.parse(res.body);
      check(body, { 'add to cart success': () => body.success === true });
      console.log(`✅ Produto adicionado ao carrinho`);
    } catch (e) {
      console.log(`❌ Resposta inválida no add to cart`);
      return;
    }
  }

  cookies = mergeCookies(cookies, res.cookies || {});

  res = http.get(BASE_URL + '/cart', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'cart page status 200': (r) => r.status === 200 });
  cookies = mergeCookies(cookies, res.cookies || {});

  console.log(`🧾 Carrinho aberto com ${Object.keys(cookies).length} cookies ativos`);

  console.log(`🔍 Enviando checkout attributes...`);

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

  if (res.status === 200) {
    try {
      let body = JSON.parse(res.body);
      console.log(`✅ Checkout attributes guardados`);
      if (body.selectedcheckoutattributesssectionhtml) {
        console.log(`📦 Resumo atributos: ${body.selectedcheckoutattributesssectionhtml}`);
      }
    } catch (e) {
      console.log(`❌ Erro ao interpretar atributos de checkout`);
    }
  }

  cookies = mergeCookies(cookies, res.cookies || {});

  res = http.get(BASE_URL + '/onepagecheckout', {
    headers: formHeaders,
    cookies: cookies,
  });
  check(res, { 'checkout page status 200': (r) => r.status === 200 });
  token = extractToken(res.body);
  console.log(`🔑 Token da checkout page: ${token ? 'SIM' : 'NÃO'}`);

  if (!token) return;

  if (shouldFail('Checkout Page', 0.05)) {
    console.log(`❌ Falha simulada na página de checkout`);
    return;
  }

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

  let billingString =
    'ShipToSameAddress=true&ShipToSameAddress=false&' +
    Object.entries(billingData)
      .filter(([key]) => key !== 'ShipToSameAddress')
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
      .join('&');

  console.log(`🔍 Enviando billing address...`);
  res = http.post(BASE_URL + '/checkout/OpcSaveBilling', billingString, {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`✅ Billing status: ${res.status}`);

  if (shouldFail('Billing Address', 0.1)) {
    console.log(`❌ Falha simulada no billing`);
    return;
  }

  cookies = mergeCookies(cookies, res.cookies || {});

  let shippingData = {
    'shippingoption': 'Ground___Shipping.FixedRate',
    '__RequestVerificationToken': token,
  };

  console.log(`🔍 Enviando shipping method...`);
  res = http.post(BASE_URL + '/checkout/OpcSaveShippingMethod', toFormBody(shippingData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`✅ Shipping status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  let paymentMethodData = {
    'paymentmethod': 'Payments.CheckMoneyOrder',
    '__RequestVerificationToken': token,
  };

  console.log(`🔍 Enviando payment method...`);
  res = http.post(BASE_URL + '/checkout/OpcSavePaymentMethod', toFormBody(paymentMethodData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`📊 Payment method status: ${res.status}`);

  if (shouldFail('Payment Method', 0.12)) {
    console.log(`❌ Falha simulada no método de pagamento`);
    return;
  }

  cookies = mergeCookies(cookies, res.cookies || {});

  let paymentInfoData = {
    'checkout_attribute_1': '1',
    '__RequestVerificationToken': token,
  };

  console.log(`🔍 Enviando payment info...`);
  res = http.post(BASE_URL + '/checkout/OpcSavePaymentInfo', toFormBody(paymentInfoData), {
    headers: formHeaders,
    cookies: cookies,
    followRedirects: false,
  });

  console.log(`📊 Payment info status: ${res.status}`);
  cookies = mergeCookies(cookies, res.cookies || {});

  let confirmData = {
    '__RequestVerificationToken': token,
  };

  console.log(`🔍 Enviando confirmação de encomenda...`);
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
        console.log(`❌ Pedido não confirmado`);
      }
    } catch (e) {
      console.log(`❌ Resposta da confirmação não é JSON`);
    }
  }

  check(confirmSuccess, { 'order confirmation success': () => confirmSuccess });

  if (shouldFail('Final Confirmation', 0.05)) {
    console.log(`❌ Falha simulada na confirmação final`);
  }

  sleep(randomIntBetween(2, 4));
}
